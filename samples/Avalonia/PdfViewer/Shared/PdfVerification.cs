#if UNO_SAMPLE
using DocumentViewer = Plugin.Uno.PdfViewer.PdfViewer;
#else
using DocumentViewer = Plugin.Avalonia.PdfViewer.PdfViewer;
#endif
namespace ReferenceSamples;

internal static class SharedPdfVerification
{
    public static async Task<string> RunAsync(
        IReadOnlyList<DocumentViewer> views,
        List<string> temporary,
        CancellationToken stopping
    )
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        using var input =
            typeof(SharedPdfVerification).Assembly.GetManifestResourceStream("Verification.pdf")
            ?? throw new InvalidOperationException("Missing verification PDF.");
        using var buffer = new MemoryStream();
        await input.CopyToAsync(buffer, token);
        var bytes = buffer.ToArray();
        string? path = null;
        if (!OperatingSystem.IsBrowser())
        {
            path = Path.Combine(Path.GetTempPath(), $"nativechannels-test-{Guid.NewGuid():N}.pdf");
            temporary.Add(path);
            await File.WriteAllBytesAsync(path, bytes, token);
        }
        async Task Ready(DocumentViewer view)
        {
            while (!view.IsConnected)
                await Task.Delay(25, token);
        }
        async Task OpenFixture(DocumentViewer view)
        {
            await Ready(view);
            if (path is null)
                await view.OpenBytesAsync(bytes, token);
            else
                await view.OpenAsync(path, token);
            var state = await view.GetStateAsync(token);
            if (state.PageCount != 3 || state.Page != 1)
                throw new InvalidOperationException("Expected the first of three PDF pages.");
        }
        foreach (var view in views)
            await OpenFixture(view);
        var first = views[0];
        var second = views[1];
        await first.GoToPageAsync(2, token);
        await first.SetZoomAsync(1.5, token);
        var a = await first.GetStateAsync(token);
        var b = await second.GetStateAsync(token);
        if (a.InstanceId == b.InstanceId || a.Page != 2 || Math.Abs(a.Zoom - 1.5) > 0.01 || b.Page != 1)
            throw new InvalidOperationException("PDF instances did not retain independent page/zoom state.");
        await first.DisconnectAsync();
        if (first.IsConnected)
            throw new InvalidOperationException("PDF did not disconnect.");
        first.Reconnect();
        await OpenFixture(first);
        if ((await first.GetStateAsync(token)).InstanceId == a.InstanceId)
            throw new InvalidOperationException("Reconnected PDF reused the disposed instance.");
        return "PASS two native PDF views, three-page document, navigation, zoom, independent state, detach and reopen";
    }
}
