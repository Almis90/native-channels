using NativeChannels;

namespace Plugin.Maui.PdfViewer;

public static class PdfViewerExtensions
{
    /// <summary>Registers the native PDF view factory from this plugin's packaged adapters.</summary>
    public static NativeChannelsOptions AddPdfViewer(this NativeChannelsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var factory = new NativeViewFactoryRegistration(
            "plugin.pdf",
            "dev.nativechannels.pdf.PdfViewFactory",
            "MCPdfViewFactory",
            "MauiPdfViewerNative"
        )
        {
#if WINDOWS
            WindowsFactory = WindowsPdfView.Create,
#endif
        };
        if (!options.ViewFactories.Contains(factory))
            options.ViewFactories.Add(factory);
        return options;
    }
}
