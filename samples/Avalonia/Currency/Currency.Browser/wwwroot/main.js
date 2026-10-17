import { dotnet } from './_framework/dotnet.js'

const is_browser = typeof window != "undefined";
if (!is_browser) throw new Error(`Expected to be running in a browser`);

const dotnetRuntime = await dotnet
    .withDiagnosticTracing(false)
    .withApplicationArgumentsFromQuery()
    .create();

const config = dotnetRuntime.getConfig();
// Explicit sample lifecycle hooks for browser verification and host-controlled teardown.
globalThis.currencyApp = (await dotnetRuntime.getAssemblyExports(config.mainAssemblyName)).Program;
globalThis.addEventListener('pagehide', () => {
    // Browsers do not guarantee asynchronous unload completion. Hosts can await CloseAsync
    // before navigation; pagehide also starts best-effort cleanup for ordinary tab closure.
    void currencyApp.CloseAsync().catch(error => console.error('Currency shutdown failed', error));
});
globalThis.addEventListener('pageshow', event => {
    if (event.persisted) globalThis.location.reload(); // Recreate a session after a BFCache restore.
});

await dotnetRuntime.runMain(config.mainAssemblyName, [globalThis.location.href]);
