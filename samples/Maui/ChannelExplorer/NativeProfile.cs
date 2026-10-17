using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Maui.Storage;

namespace ChannelExplorer;

/// <summary>Opt-in measurements through the real registered sample adapters.</summary>
internal sealed class NativeProfile(IChannelService channels, ViewVerification views)
{
    private const int Warmup = 32;
    private const int Samples = 256;
    private readonly string _runId = Metadata("NativeChannelsProfileRunId");
    private int _sequence;
    private StreamWriter? _output;
    public static int Seconds =>
        int.Parse(Metadata("NativeChannelsProfileSeconds"), System.Globalization.CultureInfo.InvariantCulture);

    private static string Metadata(string key) =>
        typeof(NativeProfile)
            .Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == key)
            .Value
        ?? "";

    public async Task RunAsync()
    {
        var path = Path.Combine(FileSystem.AppDataDirectory, "native-performance.jsonl");
#if WINDOWS || MACCATALYST
        path = Environment.GetEnvironmentVariable("NATIVECHANNELS_PROFILE_REPORT") ?? path;
#endif
        using var output = new StreamWriter(path, append: false) { AutoFlush = true };
        _output = output;
        try
        {
            Require(Seconds is >= 1 and <= 1800 && Guid.TryParse(_runId, out _), "Invalid profile configuration");
            Write(
                new()
                {
                    Kind = "start",
                    DurationSeconds = Seconds,
                    Platform = Microsoft.Maui.Devices.DeviceInfo.Current.Platform.ToString(),
                    Runtime = RuntimeInformation.FrameworkDescription + " / " + RuntimeInformation.ProcessArchitecture,
                    Os = RuntimeInformation.OSDescription,
                    FrameworkVersion = typeof(IChannelService)
                        .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                        ?.InformationalVersion,
                    AppVersion = typeof(NativeProfile)
                        .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                        ?.InformationalVersion,
                }
            );
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(Seconds + 120));
            var token = deadline.Token;
            using var methods = channels.GetMethodChannel("sample");
            using var handler = methods.SetMethodCallHandler(
                (call, _) =>
                    call.Method == "getToken"
                        ? ValueTask.FromResult<object?>("profile-token")
                        : throw new MethodNotImplementedException(call.Method)
            );
            using var messages = channels.GetBasicMessageChannel(
                "sample.messages.binary",
                new() { Codec = BinaryMessageCodec.Instance }
            );
            using var reply = messages.SetMessageHandler((value, _) => ValueTask.FromResult(value));
            var bytes = Enumerable.Range(0, 4096).Select(index => (byte)index).ToArray();

            async Task Echo() => Require(await methods.InvokeAsync<int>("echo", 42, token) == 42, "Int32 reply");
            async Task Callback() =>
                Require(
                    await methods.InvokeAsync<string>("callback", cancellationToken: token) == "native:profile-token",
                    "Reentrant method reply"
                );
            async Task Binary()
            {
                var returned = await messages.SendAsync<byte[]>(bytes, token);
                Require(returned is not null && bytes.AsSpan().SequenceEqual(returned), "Reentrant binary reply");
            }
            await MeasureAsync("method-int32", Echo);
            await MeasureAsync("method-reentrant", Callback);
            await MeasureAsync("binary-reentrant-4096", Binary);

            var timer = Stopwatch.StartNew();
            var cycles = 0;
            var nextMemory = TimeSpan.Zero;
            await MemoryAsync(methods, timer, cycles, token);
            while (timer.Elapsed.TotalSeconds < Seconds)
            {
                using (var handle = channels.GetMethodChannel("sample"))
                    Require(await handle.InvokeAsync<int>("echo", cycles, token) == cycles, "Temporary handle reply");
                await Callback();
                await Binary();
                using (var events = channels.GetEventChannel("sample.events"))
                {
                    await using var stream = events.ListenAsync<int>("live", token).GetAsyncEnumerator();
                    Require(await stream.MoveNextAsync(), "Native subscription started");
                }
                // Disposal sends remote cancellation; allow that frame to reach the native handler.
                using (var cleanup = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    cleanup.CancelAfter(TimeSpan.FromSeconds(5));
                    while (await methods.InvokeAsync<int>("activeStreams", cancellationToken: cleanup.Token) != 0)
                        await Task.Delay(10, cleanup.Token);
                }
                using (var cancel = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    var pending = methods.InvokeAsync("delay", 1000, cancel.Token);
                    cancel.CancelAfter(10);
                    try
                    {
                        await pending;
                        throw new InvalidOperationException("Cancelled call completed");
                    }
                    catch (OperationCanceledException)
                        when (cancel.IsCancellationRequested && !token.IsCancellationRequested) { }
                }
                if (cycles % 10 == 0)
                    await views.ProfileCycleAsync().WaitAsync(token);
                cycles++;
                if (timer.Elapsed >= nextMemory)
                {
                    await MemoryAsync(methods, timer, cycles, token);
                    nextMemory = timer.Elapsed + TimeSpan.FromSeconds(1);
                }
                await Task.Delay(100, token);
            }
            // Observe idle retention without forcing GC; retain raw samples, not a leak-free verdict.
            await Task.Delay(TimeSpan.FromSeconds(5), token);
            await MemoryAsync(methods, timer, cycles, token);
            Write(
                new()
                {
                    Kind = "complete",
                    ElapsedSeconds = timer.Elapsed.TotalSeconds,
                    Cycles = cycles,
                    ActiveStreams = await methods.InvokeAsync<int>("activeStreams", cancellationToken: token),
                }
            );
        }
        catch (Exception exception)
        {
            Write(new() { Kind = "error", Error = exception.GetType().Name + ": " + exception.Message });
            throw;
        }
    }

    private async Task MeasureAsync(string name, Func<Task> operation)
    {
        for (var index = 0; index < Warmup; index++)
            await operation();
        var samples = new double[Samples];
        for (var index = 0; index < Samples; index++)
        {
            var start = Stopwatch.GetTimestamp();
            await operation();
            samples[index] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }
        Array.Sort(samples);
        Write(
            new()
            {
                Kind = "timing",
                Name = name,
                Samples = Samples,
                Warmup = Warmup,
                MinMs = samples[0],
                MeanMs = samples.Average(),
                P50Ms = samples[(int)Math.Ceiling(Samples * 0.50) - 1],
                P95Ms = samples[(int)Math.Ceiling(Samples * 0.95) - 1],
                MaxMs = samples[^1],
            }
        );
    }

    private async Task MemoryAsync(MethodChannel methods, Stopwatch timer, int cycles, CancellationToken token)
    {
        var memory =
            await methods.InvokeAsync<Dictionary<string, object?>>("memory", cancellationToken: token)
            ?? throw new InvalidOperationException("Native memory sample missing");
        var bytes = memory.ToDictionary(
            pair => pair.Key,
            pair =>
                pair.Value is long value && value >= 0
                    ? value
                    : throw new InvalidOperationException("Invalid native memory metric: " + pair.Key)
        );
        Require(bytes.Count > 0, "Empty native memory metrics");
        Write(
            new()
            {
                Kind = "memory",
                ElapsedSeconds = timer.Elapsed.TotalSeconds,
                Cycles = cycles,
                ManagedBytes = GC.GetTotalMemory(false),
                AllocatedBytes = GC.GetTotalAllocatedBytes(false),
                NativeBytes = bytes,
            }
        );
    }

    private void Write(ProfileRecord record)
    {
        record.RunId = _runId;
        record.Sequence = _sequence++;
        var json = JsonSerializer.Serialize(record, ProfileJsonContext.Default.ProfileRecord);
        _output!.WriteLine(json);
        Console.WriteLine("NATIVECHANNELS_PROFILE " + json);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}

internal sealed class ProfileRecord
{
    public string Kind { get; set; } = "";
    public string RunId { get; set; } = "";
    public int Sequence { get; set; }
    public string? Platform { get; set; }
    public string? Runtime { get; set; }
    public string? Os { get; set; }
    public string? FrameworkVersion { get; set; }
    public string? AppVersion { get; set; }
    public int? DurationSeconds { get; set; }
    public double? ElapsedSeconds { get; set; }
    public int? Cycles { get; set; }
    public int? ActiveStreams { get; set; }
    public string? Name { get; set; }
    public int? Samples { get; set; }
    public int? Warmup { get; set; }
    public double? MinMs { get; set; }
    public double? MeanMs { get; set; }
    public double? P50Ms { get; set; }
    public double? P95Ms { get; set; }
    public double? MaxMs { get; set; }
    public long? ManagedBytes { get; set; }
    public long? AllocatedBytes { get; set; }
    public Dictionary<string, long>? NativeBytes { get; set; }
    public string? Error { get; set; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
)]
[JsonSerializable(typeof(ProfileRecord))]
internal partial class ProfileJsonContext : JsonSerializerContext;
