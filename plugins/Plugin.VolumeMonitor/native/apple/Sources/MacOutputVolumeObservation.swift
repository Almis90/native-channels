#if os(macOS) || targetEnvironment(macCatalyst)
    import CoreAudio
    import Foundation

    /// One subscription to the current Mac output device. All state belongs to the main queue.
    final class MacOutputVolumeObservation {
        private let onLevel: (Double) -> Void
        private let onFailure: (String) -> Void
        private var device = AudioObjectID(kAudioObjectUnknown)
        private var deviceListener: AudioObjectPropertyListenerBlock?
        private var volumeListener: AudioObjectPropertyListenerBlock?
        private var closed = false
        private var lastLevel: Double?

        init(onLevel: @escaping (Double) -> Void, onFailure: @escaping (String) -> Void) {
            self.onLevel = onLevel
            self.onFailure = onFailure
        }

        private var defaultOutput: AudioObjectPropertyAddress {
            AudioObjectPropertyAddress(
                mSelector: kAudioHardwarePropertyDefaultOutputDevice,
                mScope: kAudioObjectPropertyScopeGlobal, mElement: kAudioObjectPropertyElementMain)
        }

        private func volume(_ element: AudioObjectPropertyElement) -> AudioObjectPropertyAddress {
            AudioObjectPropertyAddress(
                mSelector: kAudioDevicePropertyVolumeScalar,
                mScope: kAudioDevicePropertyScopeOutput, mElement: element)
        }

        func start() {
            dispatchPrecondition(condition: .onQueue(.main))
            guard !closed, deviceListener == nil else { return }
            let listener: AudioObjectPropertyListenerBlock = { [weak self] _, _ in
                guard let self, !self.closed else { return }
                self.followDefaultOutput()
            }
            var address = defaultOutput
            let status = AudioObjectAddPropertyListenerBlock(
                AudioObjectID(kAudioObjectSystemObject), &address, .main, listener)
            guard status == noErr else { fail("Cannot observe the default output device", status); return }
            deviceListener = listener
            followDefaultOutput()
        }

        private func followDefaultOutput() {
            var address = defaultOutput
            var next = AudioObjectID(kAudioObjectUnknown)
            var size = UInt32(MemoryLayout<AudioObjectID>.size)
            let status = AudioObjectGetPropertyData(
                AudioObjectID(kAudioObjectSystemObject), &address, 0, nil, &size, &next)
            guard status == noErr, next != kAudioObjectUnknown else {
                fail("No default output device is available", status); return
            }
            if next != device {
                detachVolumeListener()
                device = next
                let observedDevice = next
                let listener: AudioObjectPropertyListenerBlock = { [weak self] _, _ in
                    guard let self, !self.closed, self.device == observedDevice else { return }
                    self.readLevel()
                }
                // Listen to both master and channel controls; some devices expose only channels.
                var property = volume(kAudioObjectPropertyElementWildcard)
                let registration = AudioObjectAddPropertyListenerBlock(device, &property, .main, listener)
                guard registration == noErr else { fail("Cannot observe output volume", registration); return }
                volumeListener = listener
            }
            readLevel()
        }

        private func scalar(_ element: AudioObjectPropertyElement) -> Double? {
            var address = volume(element)
            guard AudioObjectHasProperty(device, &address) else { return nil }
            var value: Float32 = 0
            var size = UInt32(MemoryLayout<Float32>.size)
            guard AudioObjectGetPropertyData(device, &address, 0, nil, &size, &value) == noErr,
                value.isFinite, (0...1).contains(value)
            else { return nil }
            return Double(value)
        }

        private func readLevel() {
            guard !closed else { return }
            let level: Double
            if let master = scalar(kAudioObjectPropertyElementMain) {
                level = master
            } else {
                var address = AudioObjectPropertyAddress(
                    mSelector: kAudioDevicePropertyPreferredChannelsForStereo,
                    mScope: kAudioDevicePropertyScopeOutput, mElement: kAudioObjectPropertyElementMain)
                var channels: [UInt32] = [1, 2]
                var size = UInt32(2 * MemoryLayout<UInt32>.size)
                let status = channels.withUnsafeMutableBytes {
                    AudioObjectGetPropertyData(device, &address, 0, nil, &size, $0.baseAddress!)
                }
                if status != noErr { channels = [1, 2] }
                let values = Set(channels).compactMap { scalar($0) }
                guard !values.isEmpty else {
                    fail("The output device does not expose a readable volume control", nil); return
                }
                level = values.reduce(0, +) / Double(values.count)
            }
            if level != lastLevel {
                lastLevel = level
                onLevel(level)
            }
        }

        private func fail(_ message: String, _ status: OSStatus?) {
            stop()
            onFailure(status.map { "\(message) (Core Audio status \($0))" } ?? message)
        }

        private func detachVolumeListener() {
            if let listener = volumeListener {
                var address = volume(kAudioObjectPropertyElementWildcard)
                AudioObjectRemovePropertyListenerBlock(device, &address, .main, listener)
                volumeListener = nil
            }
            device = AudioObjectID(kAudioObjectUnknown)
        }

        func stop() {
            dispatchPrecondition(condition: .onQueue(.main))
            guard !closed else { return }
            closed = true
            detachVolumeListener()
            if let listener = deviceListener {
                var address = defaultOutput
                AudioObjectRemovePropertyListenerBlock(
                    AudioObjectID(kAudioObjectSystemObject), &address, .main, listener)
                deviceListener = nil
            }
        }
    }
#endif
