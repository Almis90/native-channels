using System.Runtime.InteropServices;
using System.Text;
using NativeChannels;
using NativeChannels.InteropSample;

if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
{
    throw new PlatformNotSupportedException("This probe verifies Windows x64 DLL execution.");
}
if (args.Length != 1)
{
    throw new ArgumentException("Pass the directory containing the C++ and Rust probe DLLs.");
}

var directory = Path.GetFullPath(args[0]);
var implementations = new[] { (Name: "cpp", Id: 1), (Name: "rust", Id: 2) };
foreach (var implementation in implementations)
{
    var dll = Path.Combine(directory, $"native_channels_{implementation.Name}_probe.dll");
    // Reload repeatedly to exercise per-transport ownership and deterministic DLL cleanup.
    for (var cycle = 0; cycle < 10; cycle++)
    {
        NativeDllModule? module = null;
        await using (var service = CreateService(() => module = new(dll, "interop")))
        {
            using var methods = service.GetMethodChannel("interop");
            using var bytes = service.GetBasicMessageChannel(
                "interop.bytes",
                new() { Codec = BinaryMessageCodec.Instance }
            );
            Equal(implementation.Id, await methods.InvokeAsync<int>("language"), "native implementation");
            Equal((long)Environment.ProcessId, await methods.InvokeAsync<long>("processId"), "native Win32 call");
            foreach (
                var (a, b, expected) in new[]
                {
                    (20L, 22L, 42L),
                    (long.MaxValue, 0L, long.MaxValue),
                    (long.MinValue, 0L, long.MinValue),
                    (long.MinValue, long.MaxValue, -1L),
                }
            )
            {
                Equal(expected, await Add(methods, a, b), "signed 64-bit addition");
            }
            foreach (var (a, b) in new[] { (long.MaxValue, 1L), (long.MinValue, -1L) })
            {
                var error = await Throws<ChannelException>(() => Add(methods, a, b));
                Equal("native_overflow", error.Code, "native error envelope");
            }
            await Throws<MethodNotImplementedException>(() => methods.InvokeAsync("missing"));
            var missing = await Throws<ChannelException>(() =>
                methods.InvokeAsync("add", new Dictionary<string, object?> { ["a"] = 1L })
            );
            Equal("invalid_arguments", missing.Code, "missing argument");
            foreach (
                var input in new[]
                {
                    Array.Empty<byte>(),
                    new byte[] { 0, 127, 128, 255 },
                    Encoding.UTF8.GetBytes("Hello, Ελληνικά 🦀 C++\0"),
                    Enumerable.Range(0, 65536).Select(i => (byte)i).ToArray(),
                }
            )
            {
                var transformed = await bytes.SendAsync<byte[]>(input) ?? throw new Exception("Null native output.");
                if (!transformed.SequenceEqual(input.Select(value => (byte)(value ^ 0xa5))))
                {
                    throw new Exception("Native binary transformation mismatch.");
                }
                var roundTrip = await bytes.SendAsync<byte[]>(transformed) ?? throw new Exception("Null round trip.");
                if (!roundTrip.SequenceEqual(input))
                {
                    throw new Exception("Binary round trip mismatch.");
                }
            }
            // Use worker threads so the synchronous ABI is actually entered concurrently.
            await Task.WhenAll(
                Enumerable
                    .Range(0, 32)
                    .Select(i =>
                        Task.Run(async () => Equal((long)i + 7, await Add(methods, i, 7), "concurrent invocation"))
                    )
            );
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await Throws<OperationCanceledException>(() =>
                methods.InvokeAsync("processId", cancellationToken: cancelled.Token)
            );
            Equal(42L, await Add(methods, 40, 2), "channel survives errors and cancellation");
        }
        // Transport disposal already disposed the module; repeated disposal must be harmless.
        module!.Dispose();
        await using var unused = new ChannelService(new BinaryMessenger(new NativeChannelTransport()));
        await Throws<ObjectDisposedException>(() =>
        {
            module.Register(unused);
            return Task.CompletedTask;
        });
    }
    Console.WriteLine(
        $"PASS {implementation.Name}: native identity, Win32 PID, Int64 limits, overflow, binary/UTF-8, 32 concurrent calls, pre-cancellation, 10 load/dispose cycles"
    );
}

// Both native implementations can coexist with independent registrations in one transport.
await using (
    var both = new ChannelService(
        new BinaryMessenger(
            new NativeChannelTransport(
                implementations.Select(implementation => new NativeModuleRegistration("", "")
                {
                    WindowsFactory = () =>
                        new NativeDllModule(
                            Path.Combine(directory, $"native_channels_{implementation.Name}_probe.dll"),
                            implementation.Name
                        ),
                })
            )
        )
    )
)
{
    foreach (var implementation in implementations)
    {
        using var channel = both.GetMethodChannel(implementation.Name);
        Equal(implementation.Id, await channel.InvokeAsync<int>("language"), "coexisting modules");
    }
}
Console.WriteLine("PASS C++ and Rust modules coexist through the shipping Windows channel transport.");

static ChannelService CreateService(Func<IWindowsChannelModule> factory) =>
    new(new BinaryMessenger(new NativeChannelTransport([new("", "") { WindowsFactory = factory }])));

static Task<long> Add(MethodChannel channel, long a, long b) =>
    channel.InvokeAsync<long>("add", new Dictionary<string, object?> { ["a"] = a, ["b"] = b });

static void Equal<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new Exception($"{label}: expected {expected}, got {actual}.");
    }
}

static async Task<T> Throws<T>(Func<Task> action)
    where T : Exception
{
    try
    {
        await action();
    }
    catch (T error)
    {
        return error;
    }
    throw new Exception($"Expected {typeof(T).Name}.");
}
