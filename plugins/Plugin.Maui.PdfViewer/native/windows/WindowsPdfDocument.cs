#if WINDOWS
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Plugin.Maui.PdfViewer;

internal sealed class WindowsPdfDocument : IDisposable
{
    private readonly IRandomAccessStream _source;
    private readonly PdfDocument _document;
    public int PageCount => checked((int)_document.PageCount);

    private WindowsPdfDocument(IRandomAccessStream source, PdfDocument document)
    {
        _source = source;
        _document = document;
    }

    public static async Task<WindowsPdfDocument> OpenAsync(string path, CancellationToken cancellation)
    {
        if (!Path.IsPathFullyQualified(path))
            throw new ArgumentException("Use an absolute local file path.", nameof(path));
        var file = await StorageFile.GetFileFromPathAsync(path).AsTask(cancellation);
        var stream = await file.OpenReadAsync().AsTask(cancellation);
        try
        {
            var document = await PdfDocument.LoadFromStreamAsync(stream).AsTask(cancellation);
            if (document.IsPasswordProtected || document.PageCount == 0)
                throw new InvalidDataException("Supply an unencrypted PDF with at least one page.");
            return new(stream, document);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public async Task<InMemoryRandomAccessStream> RenderAsync(int pageNumber, CancellationToken cancellation)
    {
        if (pageNumber < 1 || pageNumber > PageCount)
            throw new ArgumentOutOfRangeException(nameof(pageNumber));
        using var page = _document.GetPage((uint)(pageNumber - 1));
        var scale = 1600 / Math.Max(page.Size.Width, page.Size.Height);
        var stream = new InMemoryRandomAccessStream();
        try
        {
            await page.RenderToStreamAsync(
                    stream,
                    new PdfPageRenderOptions
                    {
                        DestinationWidth = (uint)Math.Max(1, Math.Round(page.Size.Width * scale)),
                        DestinationHeight = (uint)Math.Max(1, Math.Round(page.Size.Height * scale)),
                    }
                )
                .AsTask(cancellation);
            stream.Seek(0);
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public void Dispose() => _source.Dispose();
}
#endif
