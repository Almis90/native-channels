using Plugin.Maui.PdfViewer;
using Xunit;

namespace Plugin.Maui.Windows.Tests;

public sealed class PdfViewerTests
{
    private static string Guide => Path.Combine(AppContext.BaseDirectory, "guide.pdf");

    [Fact]
    public async Task NativeRendererProducesThreeDifferentPngPages()
    {
        using var document = await WindowsPdfDocument.OpenAsync(Guide, CancellationToken.None);
        Assert.Equal(3, document.PageCount);
        var hashes = new HashSet<string>();
        for (var page = 1; page <= 3; page++)
        {
            using var rendered = await document.RenderAsync(page, CancellationToken.None);
            using var stream = rendered.AsStreamForRead();
            using var bytes = new MemoryStream();
            await stream.CopyToAsync(bytes);
            Assert.True(bytes.Length > 1000);
            Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, bytes.ToArray()[..8]);
            hashes.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes.ToArray())));
        }
        Assert.Equal(3, hashes.Count);
    }

    [Fact]
    public async Task InvalidPageAndCancelledRenderLeaveDocumentUsable()
    {
        using var document = await WindowsPdfDocument.OpenAsync(Guide, CancellationToken.None);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => document.RenderAsync(0, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => document.RenderAsync(4, CancellationToken.None));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => document.RenderAsync(1, cancelled.Token));
        using var rendered = await document.RenderAsync(2, CancellationToken.None);
        Assert.True(rendered.Size > 1000);
    }

    [Fact]
    public async Task InvalidFileFailsAndDisposalReleasesSource()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pdf");
        try
        {
            await File.WriteAllTextAsync(path, "This is not a PDF.");
            await Assert.ThrowsAnyAsync<Exception>(() => WindowsPdfDocument.OpenAsync(path, CancellationToken.None));
            File.Copy(Guide, path, true);
            using (var document = await WindowsPdfDocument.OpenAsync(path, CancellationToken.None))
                Assert.Equal(3, document.PageCount);
            using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
