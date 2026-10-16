import Foundation

public final class EventChannel {
    private let name: String
    private let bridge: NativeBridge
    public init(_ name: String, bridge: NativeBridge) {
        self.name = name
        self.bridge = bridge
    }
    public func setStreamHandler(_ handler: @escaping (Any?, EventSink) throws -> Void) -> Bool {
        let bridge = bridge
        return MethodChannel(name, bridge: bridge).setHandler(onMainThread: false) { call, result in
            guard call.method == "listen" else {
                result.notImplemented()
                return
            }
            do {
                guard let endpoint = try call.argument("events") as? String,
                    endpoint.range(of: "^__maui[.]events/[0-9a-fA-F]{32}$", options: .regularExpression) != nil
                else {
                    throw ChannelError("invalid_arguments", "Invalid event subscription endpoint")
                }
                let sink = EventSink(MethodChannel(endpoint, bridge: bridge), result: result)
                do {
                    try handler(call.argument("arguments"), sink)
                } catch let error as ChannelError {
                    sink.failure(error.code, message: error.message, details: error.details)
                } catch {
                    sink.failure("native_error", message: "Native stream handler failed")
                }
            } catch {
                result.failure("invalid_arguments", message: "Invalid event subscription", details: nil)
            }
        }
    }
}

/// Bounded to 64 queued events plus one in flight. Terminal signals do not occupy buffer slots.
public final class EventSink {
    private final class Item {
        let value: Any?
        var id: String?
        init(_ value: Any?) {
            self.value = value
        }
    }
    private let channel: MethodChannel
    private let result: MethodResult
    private let lock = NSLock()
    private var queue: [Item] = []
    private var current: Item?
    private var closed = false
    private var ending = false
    private var error: ChannelError?
    private var cleanup: (() -> Void)?
    private var cleanupRegistered = false

    init(_ channel: MethodChannel, result: MethodResult) {
        self.channel = channel
        self.result = result
        result.onCancel {
            [self] in finish(nil, cancelled: true)
        }
    }

    /// Exactly once on completion, error, cancellation, or bridge disposal, including late registration.
    public func onTermination(_ action: @escaping () -> Void) {
        lock.lock()
        precondition(!cleanupRegistered, "Cleanup is already registered")
        cleanupRegistered = true
        let run = closed
        if !run {
            cleanup = action
        }
        lock.unlock()
        if run {
            action()
        }
    }

    public func success(_ value: Any?) {
        lock.lock()
        guard !closed && !ending else {
            lock.unlock()
            return
        }
        let overflow = queue.count == 64
        if !overflow {
            queue.append(Item(value))
        }
        lock.unlock()
        if overflow {
            finish(ChannelError("event_overflow", "Native event buffer capacity 64 exceeded"))
        } else {
            pump()
        }
    }
    public func endOfStream() {
        end(nil)
    }
    public func failure(_ code: String, message: String, details: Any? = nil) {
        end(ChannelError(code, message, details: details))
    }

    private func end(_ error: ChannelError?) {
        lock.lock()
        guard !closed && !ending else {
            lock.unlock()
            return
        }
        ending = true
        self.error = error
        lock.unlock()
        pump()
    }
    private func pump() {
        lock.lock()
        guard !closed && current == nil else {
            lock.unlock()
            return
        }
        if queue.isEmpty {
            let done = ending
            let failure = error
            lock.unlock()
            if done {
                finish(failure)
            }
            return
        }
        let item = queue.removeFirst()
        current = item
        lock.unlock()
        do {
            let id = try channel.invoke("event", arguments: item.value) { [self] _, failure in
                lock.lock()
                guard !closed && current === item else {
                    lock.unlock()
                    return
                }
                current = nil
                lock.unlock()
                if let failure {
                    finish(failure)
                } else {
                    pump()
                }
            }
            lock.lock()
            let cancel = closed
            if current === item {
                item.id = id
            }
            lock.unlock()
            if cancel {
                channel.cancel(id)
            }
        } catch {
            finish(ChannelError("serialization_error", "Cannot encode event"))
        }
    }
    private func finish(_ error: ChannelError?, cancelled: Bool = false) {
        lock.lock()
        guard !closed else {
            lock.unlock()
            return
        }
        closed = true
        queue.removeAll()
        let action = cleanup
        cleanup = nil
        let request = current?.id
        current = nil
        lock.unlock()
        if let request {
            channel.cancel(request)
        }
        action?()
        if !cancelled {
            if let error {
                result.failure(error.code, message: error.message, details: error.details)
            } else {
                result.success(nil)
            }
        }
    }
}
