#if UNO_BROWSER_SAMPLE
using System.Runtime.InteropServices.JavaScript;

namespace UnoSamples;

internal static partial class BrowserVerification
{
    public static async Task RegisterAsync(Func<Task<string>> verify)
    {
        using var document = JSHost.GlobalThis.GetPropertyAsJSObject("document")!;
        await JSHost.ImportAsync(
            "NativeChannels.Uno.Sample",
            new Uri(new Uri(document.GetPropertyAsString("baseURI")!), "./browser-verification.mjs").AbsoluteUri
        );
        Register(async () =>
        {
            string result;
            try
            {
                result = await verify();
            }
            catch (Exception error)
            {
                result = "FAIL " + error;
            }
            Complete(result);
        });
    }

    [JSImport("register", "NativeChannels.Uno.Sample")]
    private static partial void Register([JSMarshalAs<JSType.Function>] Action verify);

    [JSImport("complete", "NativeChannels.Uno.Sample")]
    private static partial void Complete(string result);
}
#endif
