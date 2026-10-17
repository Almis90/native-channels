import AVFoundation
import Foundation
import NativeChannelsNative

@objc(MCVolumeMonitorModule)
public final class VolumeMonitorModule: NSObject, NativeModule {
    public required override init() { super.init() }
    public func register(with bridge: NativeBridge) {
        precondition(
            EventChannel("plugin.maui.volume", bridge: bridge).setStreamHandler { _, sink in
                let subscription = VolumeObservation(sink)
                sink.onTermination {
                    DispatchQueue.main.async { subscription.stop() }
                }
                DispatchQueue.main.async { subscription.start() }
            })
    }
}

/// Uses Core Audio on Mac and the host app's audio session on iOS.
private final class VolumeObservation {
    private let sink: EventSink
    #if targetEnvironment(macCatalyst)
        private var observation: MacOutputVolumeObservation?
    #else
        private var observation: NSKeyValueObservation?
    #endif
    private var closed = false
    private var lastLevel: Double?
    init(_ sink: EventSink) { self.sink = sink }

    func start() {
        guard !closed else { return }
        #if targetEnvironment(macCatalyst)
            let observer = MacOutputVolumeObservation(
                onLevel: { [weak self] in self?.emit($0) },
                onFailure: { [weak self] message in
                    guard let self, !self.closed else { return }
                    self.stop()
                    self.sink.failure("volume_unavailable", message: message)
                })
            observation = observer
            observer.start()
        #else
            observation = AVAudioSession.sharedInstance().observe(\.outputVolume, options: [.initial, .new]) {
                [weak self] session, change in
                let level = Double(change.newValue ?? session.outputVolume)
                DispatchQueue.main.async { [weak self] in self?.emit(level) }
            }
        #endif
    }

    private func emit(_ level: Double) {
        guard !closed else { return }
        guard level.isFinite, (0...1).contains(level) else {
            stop()
            sink.failure("volume_unavailable", message: "Cannot read the output volume")
            return
        }
        if lastLevel != level {
            lastLevel = level
            sink.success(level)
        }
    }

    func stop() {
        guard !closed else { return }
        closed = true
        #if targetEnvironment(macCatalyst)
            observation?.stop()
        #else
            observation?.invalidate()
        #endif
        observation = nil
    }
}
