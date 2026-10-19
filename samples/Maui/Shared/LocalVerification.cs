using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using NativeChannels;

namespace NativeSamples;

internal static class LocalVerification
{
    public static bool Enabled =>
#if ANDROID
        Platform.CurrentActivity?.Intent?.GetBooleanExtra("verify", false) == true;
#elif IOS || MACCATALYST
        Foundation.NSProcessInfo.ProcessInfo.Arguments.Contains("--verify");
#else
        Environment.GetCommandLineArgs().Contains("--verify");
#endif

    private static bool _reported;

    public static async Task<bool> CompleteAsync(Page page, string result, Func<Task>? cleanup = null)
    {
        if (!Enabled)
            return false;
        if (_reported)
            throw new InvalidOperationException("Local verification must report exactly once.");
        _reported = true;
        try
        {
            try
            {
                if (cleanup is not null)
                    await cleanup();
            }
            finally
            {
                var services =
                    page.Handler?.MauiContext?.Services
                    ?? throw new InvalidOperationException("Missing MAUI services during cleanup.");
                await ((IAsyncDisposable)services.GetRequiredService<IChannelService>()).DisposeAsync();
            }
            Console.WriteLine("NATIVECHANNELS_MAUI_CLOSED");
        }
        catch (Exception error)
        {
            result = "FAIL cleanup: " + error + "; " + result;
        }
        result = result.Replace('\r', ' ').Replace('\n', ' ');
        Console.WriteLine("NATIVECHANNELS_MAUI_RESULT " + result);
        if (Environment.GetEnvironmentVariable("NATIVECHANNELS_SAMPLE_RESULT") is { Length: > 0 } path)
        {
            File.WriteAllText(path + ".tmp", result);
            File.Move(path + ".tmp", path, overwrite: true);
        }
#if WINDOWS || MACCATALYST
        Environment.ExitCode = result.StartsWith("PASS ", StringComparison.Ordinal) ? 0 : 1;
        MainThread.BeginInvokeOnMainThread(() => Application.Current!.Quit());
#endif
        return true;
    }
}
