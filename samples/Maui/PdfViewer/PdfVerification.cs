using NativeChannels;
#if UNO_SAMPLE
using MainThread = UnoSamples.PdfVerificationThread;
using Plugin.Uno.PdfViewer;
using DocumentViewer = Plugin.Uno.PdfViewer.PdfViewer;
#else
using Microsoft.Maui.ApplicationModel;
using Plugin.Maui.PdfViewer;
using DocumentViewer = Plugin.Maui.PdfViewer.PdfViewer;
#endif

namespace PdfViewer.Sample;

internal static class PdfVerification
{
    public static async Task<string> RunAsync(DocumentViewer first, DocumentViewer second, string path)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var token = deadline.Token;
        var pageChanged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed(object? sender, PdfState state)
        {
            if (!MainThread.IsMainThread)
                pageChanged.TrySetException(new InvalidOperationException("Callback must use the UI thread"));
            if (state.Page == 2)
                pageChanged.TrySetResult();
        }
        first.StateChanged += Changed;
        try
        {
            var initial = await first.OpenAsync(path, token);
            Check(initial.Page == 1 && initial.PageCount == 3, "three-page PDF");
            var other = await second.OpenAsync(path, token);
            Check(initial.InstanceId != other.InstanceId, "independent native instances");
            Check((await first.GoToPageAsync(2, token)).Page == 2, "page navigation");
            await pageChanged.Task.WaitAsync(token);
            Check((await second.GetStateAsync(token)).Page == 1, "independent page state");
            Check(Math.Abs((await first.SetZoomAsync(2, token)).Zoom - 2) < 0.02, "native zoom");
            await ExpectError("invalid_page", () => first.GoToPageAsync(4, token));
            await ExpectError("pdf_open_failed", () => first.OpenAsync(path + ".missing", token));
            Check((await first.GetStateAsync(token)).Page == 2, "failed open preserves document");
            first.Reconnect();
            var empty = await first.GetStateAsync(token);
            Check(empty.InstanceId != initial.InstanceId && empty.PageCount == 0, "reconnect releases document");
            Check((await second.GetStateAsync(token)).PageCount == 3, "other document survives reconnect");
            await first.OpenAsync(path, token);
            // Detach while native work is queued: the old connection must never update a replacement.
            var pending = first.OpenAsync(path, token);
            await first.DisconnectAsync();
            try
            {
                await pending;
            }
            catch (ObjectDisposedException) { }
            Check(!first.IsConnected && first.State is null, "detach clears state");
            first.Reconnect();
            Check((await first.GetStateAsync(token)).PageCount == 0, "late work ignored");
            await first.OpenAsync(path, token);
            return "document, navigation, zoom, native callback, isolation, errors and detach/reconnect";
        }
        finally
        {
            first.StateChanged -= Changed;
        }
    }

    private static void Check(bool condition, string name)
    {
        if (!condition)
            throw new InvalidOperationException("PDF verification failed: " + name);
    }

    private static async Task ExpectError(string code, Func<Task<PdfState>> action)
    {
        try
        {
            await action();
        }
        catch (ChannelException error) when (error.Code == code)
        {
            return;
        }
        throw new InvalidOperationException("Expected native error " + code);
    }
}
