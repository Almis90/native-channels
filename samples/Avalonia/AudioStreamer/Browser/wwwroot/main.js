import { dotnet } from './_framework/dotnet.js';
const runtime = await dotnet.withDiagnosticTracing(false).create();
const config = runtime.getConfig();
const exports = await runtime.getAssemblyExports(config.mainAssemblyName);
document.addEventListener('visibilitychange', () => { if (document.hidden) void exports.ReferenceSamples.Program.SuspendAsync(); });
window.addEventListener('pagehide', () => { void exports.ReferenceSamples.Program.CloseAsync(); });
await runtime.runMain();
