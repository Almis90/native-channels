using Microsoft.Extensions.DependencyInjection;
using NativeChannels;
using Plugin.AudioStreamer;
using Plugin.Currency;
using Plugin.Uno.PdfViewer;
using Plugin.VolumeMonitor;

namespace UnoFeasibility;

internal static class PluginProbe
{
    public static async Task RunAsync(Window window, ContentPresenter surface)
    {
        await using var host = UnoChannelHost.Create(services =>
        {
            services.AddNativeChannels(options => options.AddCurrency().AddAudioStreamer().AddVolumeMonitor());
            services.AddCurrency().AddAudioStreamer().AddVolumeMonitor();
        });
        await using var session = host.CreateSession(window);
        var currency = session.Services.GetRequiredService<ICurrencyService>();
        Require(await currency.GetSymbolAsync("EUR") == "€", "native currency EUR");
        Require(!string.IsNullOrWhiteSpace(await currency.GetSymbolAsync("USD")), "native currency USD");
        try
        {
            await currency.GetSymbolAsync("ZZZ");
            throw new InvalidOperationException("Expected unknown currency error");
        }
        catch (ChannelException error) when (error.Code == "invalid_currency_code") { }

        var volume = session.Services.GetRequiredService<IVolumeMonitor>();
        await using var observer = volume.ListenAsync(session.Stopping).GetAsyncEnumerator();
        Require(await observer.MoveNextAsync() && observer.Current is >= 0 and <= 1, "native output volume");

        var audio = session.Services.GetRequiredService<IAudioStreamer>();
        var playback = await audio.OpenAsync(session.Stopping);
        // Silence validates the native buffer path without producing an audible test tone.
        await playback.WriteAsync(new byte[960], session.Stopping);
        await playback.PauseAsync(session.Stopping);
        Require((await playback.GetStatusAsync()).State == "paused", "foreground audio pause");
        await playback.ResumeAsync(session.Stopping);
        Require((await playback.GetStatusAsync()).State == "playing", "foreground audio resume");

        if (!OperatingSystem.IsMacOS())
            await PdfProtocolProbe.RunAsync(surface);
        await session.DisposeAsync();
        Require(playback.IsDisposed && session.Stopping.IsCancellationRequested, "plugin session cleanup");
    }

    private static void Require(bool value, string operation)
    {
        if (!value)
            throw new InvalidOperationException("Uno plugin probe failed: " + operation);
    }
}
