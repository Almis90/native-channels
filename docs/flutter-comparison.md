# Relationship to Flutter channels

NativeChannels adopts the named-channel and asynchronous request/response idea. It runs in .NET MAUI,
without a Flutter engine or Dart runtime. Its JSON tags, framing, and native module API are its own.
Flutter plugin binaries and wire codecs cannot be reused directly.

Write SDK adapters in Kotlin/Java or Swift/Objective-C, package their dependencies, and register the
adapter module. C# references a reusable generic binding instead of generating an SDK-specific binding.
This development version provides MethodChannel, BasicMessageChannel, and EventChannel. Native view channels
follow separate release gates; no placeholder public APIs are shipped for them.
