# Contributing

Use the pinned tools and setup commands in [building](docs/building.md). The portable solution
runs without MAUI workloads:

```sh
bash eng/bootstrap.sh
bash eng/dotnet.sh test -c Release
```

Keep SDK-specific operations in native adapters and the managed binding surface generic. Preserve
channel-handle and registration ownership, cooperative cancellation, and callback cleanup. Add
regression coverage for changes to protocol or lifecycle behavior, and update the relevant docs.

Changes to bridges, native dependencies, binding definitions, or packaging require rebuilding the
native artifacts, repacking, and verifying the separate consumer from a fresh NuGet cache. Run both
Android and iOS simulator Release round trips. Record compile-only targets separately from executed
targets; simulator results do not establish physical-device support.

Do not commit generated packages, build outputs, local SDKs, logs, signing material, or machine-specific
settings. The repository's GitHub Actions workflows produce validation artifacts without publishing
packages or creating releases.

For bug reports, include a minimal reproduction, package/tool versions, platform and runtime identifier,
Debug/Release configuration, and the relevant exception or native log. See
[troubleshooting](docs/troubleshooting.md) for collecting a sample result.


## Readability and formatting

Use one statement per line, explicit C# control-flow braces, and named methods for distinct steps.
Keep lock boundaries and cancellation cleanup visible. Avoid nesting serialization, transport calls,
and response decoding into one expression. Sample pages should display results; verification belongs
in a separate class with named scenarios.

The repository pins [CSharpier](https://csharpier.com/) in `.config/dotnet-tools.json` and
[ktfmt](https://github.com/Kotlin/ktfmt) 0.54 in the formatting script. Swift uses the `swift-format`
bundled with the pinned Xcode, with settings in `.swift-format`.

```sh
bash eng/format.sh csharp          # C#, project files, and solutions; no MAUI workloads needed
bash eng/format.sh kotlin          # Kotlin and Gradle scripts; JDK required
bash eng/format.sh swift           # Swift; Xcode required
bash eng/format.sh all --check     # Check without rewriting source files
```

The helper restores CSharpier locally and downloads a checksum-verified ktfmt jar under `.tools`.
Each language accepts `--check`; Automatic CI checks C#; the manual release workflow checks Kotlin and Swift.
Keep formatting changes separate from behavioral changes, and preserve the existing tests during cleanup.

## Documentation changes

Keep the [README](README.md) focused on the first successful integration and link to focused guides
from the [documentation index](docs/overview.md). Use the real sample's method/module names in quick-start
examples. Check API signatures against the C#, Kotlin, and Swift implementations, and run complete
portable examples when changing them. Keep local-feed instructions explicit until packages are published.

Record new platform evidence in [verification](docs/verification.md), linking the successful job rather
than treating a compile-only or partially failed workflow as a runtime pass. Do not move an existing
release tag when updating documentation on the main branch.

The GitHub Pages site uses VitePress. With Node.js 22 or newer:

```sh
npm ci
npm run docs:dev
npm run docs:build
npm run docs:preview
```

The production build checks local page links, anchors, and assets. Plugin and sample pages include
their existing READMEs, so edit the original files. The documentation workflow validates pull requests
and deploys `main` to GitHub Pages; NuGet publication remains separate. See the
[site maintenance guide](docs/documentation-site.md) for routing and deployment details.
