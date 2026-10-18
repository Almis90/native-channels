# VolumeMonitor sample

A package-only MAUI consumer of the VolumeMonitor reference plugin. It displays native media/output
volume as a percentage and animated bar, keeps the last 12 events, and provides Start/Stop controls.
The initial check exercises independent subscriptions, initial values, cancellation, and restart;
then the app starts listening. It logs `VOLUME_SAMPLE PASS …` and writes `volume-result.txt`.

See the [plugin guide](../../../plugins/Plugin.Maui.VolumeMonitor/README.md) for local build/run commands,
platform behavior, audio session ownership, and limitations. Android hardware buttons control the
music stream while this activity is visible. The Apple sample owns an ambient audio session for its
foreground lifetime, including while listening is stopped, to keep iOS volume state current between
subscriptions. Stop removes the observer; backgrounding stops listening and deactivates the session.
Returning activates the session but leaves listening stopped. The plugin does not configure audio sessions.

Use a physical iPhone to verify actual volume changes; a simulator's initial value does not establish
KVO change delivery. Android Settings notifications are best effort and may vary by device. Neither
platform reports every physical button press. Run Android and iOS simulators separately.
