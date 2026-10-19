namespace UnoSamples;

internal sealed partial class SamplePage
{
    private const string Title = "PdfViewer";
    private const string Limitation = "No native PDF adapter is bundled. Protocol checks do not render a document.";

    private void Build()
    {
        _status.Text = Limitation;
        Button(
            "Check PDF control protocol",
            async () =>
            {
                await VerifyAsync();
                _status.Text = "Protocol passed: page, zoom, callbacks, identity, reconnect and cleanup. " + Limitation;
            }
        );
    }

    private Task VerifyAsync() => UnoFeasibility.PdfProtocolProbe.RunAsync(_surface);

    public Task StopAsync() => Task.CompletedTask;
}
