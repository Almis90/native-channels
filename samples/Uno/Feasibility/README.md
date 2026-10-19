# Uno 0.3 feasibility spike

This is an isolated investigation, not `NativeChannels.Uno` or a supported sample.
It pins Uno.Sdk **6.7.30** (Uno runtime **6.7.135**) and .NET SDK **10.0.401**.
The application originated from the matching blank Uno template. Template heads
exist for Android, iOS, browser and desktop; only the Windows desktop probe has
been built and executed so far. No library project references are used.

Run the desktop gate on Windows from the repository root:

```powershell
pwsh -File eng/verify-uno-feasibility.ps1
```

This rebuilds Core and the current portable Linux transport into a local feed,
restores the Uno desktop consumer into a fresh cache, and records dependency and
native-view results under `artifacts/uno-feasibility/runs/`. A successful command
means that evidence was collected, not that the integration works. Inspect the
individual JSON statuses. The runner does not publish packages or start CI.

For a diagnostic rerun when disk space is limited, pass
`-ReuseCache artifacts/uno-feasibility/cache`. This mode is recorded in
`restore-mode.json` and is not fresh-cache acceptance.

The dependency probe executes transport initialization and inspects the released
macOS runtime's type metadata without loading macOS native code. The hidden
Windows UI probe creates a real Win32 button, asks Uno to embed it, checks its
parent, destroys it, and repeats with a new button. It does not verify visual
fidelity, input, focus, or channel communication. Other heads currently report
that their view probe is unimplemented; they are not acceptance tests.

See [the feasibility findings](../../../docs/uno-feasibility.md) for blockers,
the target matrix, and the work needed before full six-platform acceptance.
