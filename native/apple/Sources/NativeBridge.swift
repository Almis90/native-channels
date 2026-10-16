import Foundation

@objc(MCReply)
public protocol Reply: AnyObject {
    func complete(_ status: Int32, payload: Data)
}

@objc(MCMessageHandler)
public protocol MessageHandler: AnyObject {
    func receive(_ channel: String, payload: Data, reply: Reply)
}

@objc(MCNativeModule)
public protocol NativeModule: AnyObject {
    init()
    func register(with bridge: NativeBridge)
}

@objc(MCCancellationHandler)
public protocol CancellationHandler: AnyObject {
    func onCancel()
}

@objc(MCRequestContext)
public protocol RequestContext: Reply {
    var requestId: String { get }
    var isCancelled: Bool { get }
    func onCancelled(_ handler: CancellationHandler)
}

@objc(MCRequestHandler)
public protocol RequestHandler: AnyObject {
    func receiveRequest(_ channel: String, payload: Data, context: RequestContext)
}

private final class OnceReply: NSObject, RequestContext {
    private let lock = NSLock()
    private var callback: Reply?
    private let finished: () -> Void
    private var cancellation: CancellationHandler?
    private var cancelled = false
    let requestId: String
    var isCancelled: Bool {
        lock.lock()
        defer {
            lock.unlock()
        }
        return cancelled
    }

    init(_ id: String, _ callback: Reply, finished: @escaping () -> Void) {
        requestId = id
        self.callback = callback
        self.finished = finished
    }

    func onCancelled(_ handler: CancellationHandler) {
        lock.lock()
        let notify = cancelled
        if !notify && callback != nil {
            cancellation = handler
        }
        lock.unlock()
        if notify {
            handler.onCancel()
        }
    }

    func cancel(_ status: Int32 = 4) {
        lock.lock()
        cancelled = true
        let handler = cancellation
        cancellation = nil
        lock.unlock()
        handler?.onCancel()
        complete(status, payload: Data())
    }

    func complete(_ status: Int32, payload: Data) {
        lock.lock()
        let target = callback
        callback = nil
        cancellation = nil
        lock.unlock()
        if let target {
            finished()
            target.complete(
                payload.count > 16 * 1024 * 1024 ? 2 : status,
                payload: payload.count > 16 * 1024 * 1024 ? Data() : Data(payload))
        }
    }
}

@objc(MCNativeBridge)
public final class NativeBridge: NSObject {
    private let lock = NSLock()
    private let queue = DispatchQueue(label: "dev.nativechannels.dispatch")
    private var handlers: [String: MessageHandler] = [:]
    private var receiver: MessageHandler?
    private var requestHandlers: [String: RequestHandler] = [:]
    private var requestReceiver: RequestHandler?
    private var closed = false
    private var pending: [String: OnceReply] = [:]

    @objc public var protocolVersion: Int32 {
        1
    }

    @objc public func setRequestReceiver(_ handler: RequestHandler?) {
        lock.lock()
        defer {
            lock.unlock()
        }
        if !closed {
            requestReceiver = handler
        }
    }

    @objc public func registerRequest(_ channel: String, handler: RequestHandler) -> Bool {
        lock.lock()
        defer {
            lock.unlock()
        }
        guard !closed, validName(channel), handlers[channel] == nil, requestHandlers[channel] == nil else {
            return false
        }
        requestHandlers[channel] = handler
        return true
    }

    @objc public func sendRequest(_ id: String, channel: String, payload: Data, reply: Reply) {
        dispatch(id, channel, payload: payload, reply: reply, toManaged: false)
    }

    @objc public func invokeManagedRequest(_ id: String, channel: String, payload: Data, reply: Reply) {
        dispatch(id, channel, payload: payload, reply: reply, toManaged: true)
    }

    @objc public func cancel(_ id: String) {
        lock.lock()
        let request = pending[id]
        lock.unlock()
        request?.cancel()
    }

    @objc public override init() {
        super.init()
    }

    @objc public func setReceiver(_ handler: MessageHandler?) {
        lock.lock()
        defer {
            lock.unlock()
        }
        if !closed {
            receiver = handler
        }
    }

    @objc public func register(_ channel: String, handler: MessageHandler) -> Bool {
        lock.lock()
        defer {
            lock.unlock()
        }
        guard !closed, validName(channel), handlers[channel] == nil, requestHandlers[channel] == nil else {
            return false
        }
        handlers[channel] = handler
        return true
    }

    @objc public func loadModule(_ className: String) -> String? {
        guard let type = NSClassFromString(className) as? NativeModule.Type else {
            return "Native module not found or does not conform to MCNativeModule: \(className)"
        }
        type.init().register(with: self)
        return nil
    }

    @objc public func send(_ channel: String, payload: Data, reply: Reply) {
        dispatch(UUID().uuidString, channel, payload: payload, reply: reply, toManaged: false)
    }

    @objc public func invokeManaged(_ channel: String, payload: Data, reply: Reply) {
        dispatch(UUID().uuidString, channel, payload: payload, reply: reply, toManaged: true)
    }

    private func validName(_ name: String) -> Bool {
        !name.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty && name.utf8.count <= 256
            && name.rangeOfCharacter(from: .controlCharacters) == nil
    }

    private func dispatch(_ id: String, _ channel: String, payload: Data, reply: Reply, toManaged: Bool) {
        guard validName(channel), payload.count <= 16 * 1024 * 1024, UUID(uuidString: id) != nil else {
            reply.complete(2, payload: Data())
            return
        }
        let copy = Data(payload)
        lock.lock()
        guard !closed, pending[id] == nil else {
            let status: Int32 = closed ? 3 : 2
            lock.unlock()
            reply.complete(status, payload: Data())
            return
        }
        let once = OnceReply(id, reply) {
            [weak self] in
            guard let self else {
                return
            }
            self.lock.lock()
            self.pending.removeValue(forKey: id)
            self.lock.unlock()
        }
        pending[id] = once
        lock.unlock()
        queue.async {
            [self] in
            lock.lock()
            let isClosed = closed
            let handler = toManaged ? receiver : handlers[channel]
            let requestHandler = toManaged ? requestReceiver : requestHandlers[channel]
            lock.unlock()
            if isClosed {
                once.complete(3, payload: Data())
                return
            }
            if once.isCancelled {
                return
            }
            if let requestHandler {
                requestHandler.receiveRequest(channel, payload: copy, context: once)
                return
            }
            guard let handler else {
                once.complete(1, payload: Data())
                return
            }
            handler.receive(channel, payload: copy, reply: once)
        }
    }

    @objc public func close() {
        lock.lock()
        closed = true
        receiver = nil
        requestReceiver = nil
        handlers.removeAll()
        requestHandlers.removeAll()
        let outstanding = Array(pending.values)
        pending.removeAll()
        lock.unlock()
        for reply in outstanding {
            reply.cancel(3)
        }
    }

    @objc public func closeAsync(_ completion: Reply) {
        close()
        queue.async {
            completion.complete(0, payload: Data())
        }
    }
}
