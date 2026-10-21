namespace Plugin.Uno.PdfViewer;

public enum LinuxPdfBackend
{
    Qt,
    Gtk,
}

public static class PdfLinux
{
    public static LinuxPdfBackend Backend { get; set; } = LinuxPdfBackend.Qt;
}
