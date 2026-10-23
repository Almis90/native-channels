import AVFoundation
import Foundation
import NativeChannelsNative

@objc(MCAudioStreamerModule)
public final class AudioStreamerModule: NSObject, NativeModule {
    public required override init() {
        super.init()
    }
    public func register(with bridge: NativeBridge) {
        let player = PcmPlayer()
        precondition(
            MethodChannel("plugin.maui.audio.control", bridge: bridge).setHandler(onMainThread: false) {
                call, result in player.control(call, result)
            })
        precondition(
            BasicMessageChannel("plugin.maui.audio.pcm", bridge: bridge, codec: BinaryMessageCodec()).setHandler {
                value, result in player.enqueue(value as? Data, result)
            })
    }
}

private final class PcmPlayer {
    private let queue = DispatchQueue(label: "dev.nativechannels.audio")
    private var engine: AVAudioEngine?
    private var player: AVAudioPlayerNode?
    private var format: AVAudioFormat?
    private var lease: MethodResult?
    private var identity: Data?
    private var state = "stopped"
    private var buffered: Int32 = 0
    private var played: Int64 = 0
    private var observers: [NSObjectProtocol] = []

    func control(_ call: MethodCall, _ result: MethodResult) {
        queue.async { [self] in
            guard !result.isCancelled else {
                return
            }
            do {
                guard let token = try call.argument("session") as? Data, token.count == 16 else {
                    throw ChannelError("invalid_arguments", "Expected a 16-byte audio session ID")
                }
                if call.method == "open" {
                    try open(token, result)
                    return
                }
                if let identity, identity != token {
                    throw ChannelError("audio_session_mismatch", "This audio session is no longer current")
                }
                switch call.method {
                case "status":
                    result.success([
                        "state": state, "bufferedFrames": buffered, "capacityFrames": Int32(24000),
                        "playedFrames": played,
                    ])
                case "pause":
                    try requireActive()
                    player!.pause()
                    state = "paused"
                    result.success(nil)
                case "resume":
                    try requireActive()
                    player!.play()
                    state = "playing"
                    result.success(nil)
                case "stop":
                    close()
                    result.success(nil)
                default: result.notImplemented()
                }
            } catch let error as ChannelError {
                result.failure(error.code, message: error.message, details: nil)
            } catch {
                close(ChannelError("audio_device_error", "Native audio operation failed"))
                result.failure("audio_device_error", message: "Native audio operation failed", details: nil)
            }
        }
    }

    private func requireActive() throws {
        if player == nil {
            throw ChannelError("audio_closed", "No active audio session")
        }
    }

    private func open(_ token: Data, _ result: MethodResult) throws {
        guard lease == nil else {
            throw ChannelError("audio_busy", "An audio session is already open")
        }
        identity = token
        lease = result
        result.onCancel { [self] in
            queue.async { [self] in
                if identity == token {
                    close()
                }
            }
        }
        do {
            #if !os(macOS)
                let session = AVAudioSession.sharedInstance()
                try session.setCategory(.playback, mode: .default)
                try session.setActive(true)
            #endif
            let audio = AVAudioEngine()
            let node = AVAudioPlayerNode()
            let pcm = AVAudioFormat(standardFormatWithSampleRate: 48000, channels: 1)!
            engine = audio
            player = node
            format = pcm
            audio.attach(node)
            audio.connect(node, to: audio.mainMixerNode, format: pcm)
            try audio.start()
            node.play()
            buffered = 0
            played = 0
            state = "playing"
            let notifications = NotificationCenter.default
            #if !os(macOS)
                observers.append(
                    notifications.addObserver(
                        forName: AVAudioSession.interruptionNotification, object: session, queue: nil
                    ) {
                        [weak self] notification in
                        guard let type = notification.userInfo?[AVAudioSessionInterruptionTypeKey] as? UInt,
                            type == AVAudioSession.InterruptionType.began.rawValue
                        else {
                            return
                        }
                        self?.interrupt(token)
                    })
                observers.append(
                    notifications.addObserver(
                        forName: AVAudioSession.routeChangeNotification, object: session, queue: nil
                    ) {
                        [weak self] notification in
                        guard let reason = notification.userInfo?[AVAudioSessionRouteChangeReasonKey] as? UInt,
                            reason == AVAudioSession.RouteChangeReason.oldDeviceUnavailable.rawValue
                        else {
                            return
                        }
                        self?.interrupt(token)
                    })
            #endif
            observers.append(
                notifications.addObserver(forName: .AVAudioEngineConfigurationChange, object: audio, queue: nil) {
                    [weak self] _ in self?.interrupt(token)
                })
        } catch {
            close(ChannelError("audio_device_error", "Cannot initialize AVAudioEngine"))
        }
    }

    private func interrupt(_ token: Data) {
        queue.async { [self] in
            if identity == token {
                close(ChannelError("audio_interrupted", "Audio route or session changed. Start playback again"))
            }
        }
    }

    func enqueue(_ payload: Data?, _ result: MessageResult) {
        queue.async { [self] in
            guard let payload, payload.count >= 18, payload.count <= 9616, (payload.count - 16) % 2 == 0 else {
                result.success(Data([4]))
                return
            }
            guard let identity, payload.prefix(16) == identity else {
                result.success(Data([3]))
                return
            }
            if state == "paused" {
                result.success(Data([2]))
                return
            }
            guard state == "playing", let player, let format else {
                result.success(Data([3]))
                return
            }
            let frames = (payload.count - 16) / 2
            guard buffered + Int32(frames) <= 24000 else {
                result.success(Data([1]))
                return
            }
            guard let buffer = AVAudioPCMBuffer(pcmFormat: format, frameCapacity: AVAudioFrameCount(frames)),
                let samples = buffer.floatChannelData?[0]
            else {
                result.failure()
                return
            }
            buffer.frameLength = AVAudioFrameCount(frames)
            payload.withUnsafeBytes { raw in
                let bytes = raw.bindMemory(to: UInt8.self)
                for index in 0..<frames {
                    let offset = 16 + index * 2
                    let bits = UInt16(bytes[offset]) | (UInt16(bytes[offset + 1]) << 8)
                    samples[index] = Float(Int16(bitPattern: bits)) / 32768
                }
            }
            buffered += Int32(frames)
            let token = identity
            player.scheduleBuffer(buffer, completionCallbackType: .dataPlayedBack) { [weak self] _ in
                guard let self else {
                    return
                }
                self.queue.async { [self] in
                    guard self.identity == token else {
                        return
                    }
                    self.buffered -= Int32(frames)
                    self.played += Int64(frames)
                }
            }
            result.success(Data([0]))
        }
    }

    private func close(_ error: ChannelError? = nil) {
        guard lease != nil || engine != nil else {
            return
        }
        let completion = lease
        lease = nil
        identity = nil
        state = "stopped"
        for observer in observers {
            NotificationCenter.default.removeObserver(observer)
        }
        observers.removeAll()
        player?.stop()
        engine?.stop()
        player = nil
        engine = nil
        format = nil
        buffered = 0
        played = 0
        #if !os(macOS)
            try? AVAudioSession.sharedInstance().setActive(false, options: .notifyOthersOnDeactivation)
        #endif
        if let completion, !completion.isCancelled {
            if let error {
                completion.failure(error.code, message: error.message, details: nil)
            } else {
                completion.success(nil)
            }
        }
    }
}
