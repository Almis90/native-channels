import Foundation

public protocol MessageCodec {
    func encode(_ value: Any?) throws -> Data
    func decode(_ data: Data) throws -> Any?
}

public struct JsonMessageCodec: MessageCodec {
    public init() {}
    public func encode(_ value: Any?) throws -> Data {
        try JSONSerialization.data(withJSONObject: JsonMethodCodec.encodeValue(value))
    }
    public func decode(_ data: Data) throws -> Any? {
        guard data.count <= 16 * 1024 * 1024 else {
            throw invalidMessage()
        }
        return try JsonMethodCodec.decodeValue(JSONSerialization.jsonObject(with: data))
    }
}

public struct BinaryMessageCodec: MessageCodec {
    public init() {}
    public func encode(_ value: Any?) throws -> Data {
        guard let value, !(value is NSNull) else {
            return Data([0])
        }
        guard let bytes = value as? Data else {
            throw invalidMessage()
        }
        return Data([1]) + bytes
    }
    public func decode(_ data: Data) throws -> Any? {
        guard data.count <= 16 * 1024 * 1024, let marker = data.first else {
            throw invalidMessage()
        }
        if marker == 0 {
            guard data.count == 1 else {
                throw invalidMessage()
            }
            return nil
        }
        guard marker == 1 else {
            throw invalidMessage()
        }
        return Data(data.dropFirst())
    }
}

public struct StringMessageCodec: MessageCodec {
    public init() {}
    public func encode(_ value: Any?) throws -> Data {
        guard let value, !(value is NSNull) else {
            return Data([0])
        }
        guard let text = value as? String else {
            throw invalidMessage()
        }
        return Data([1]) + Data(text.utf8)
    }
    public func decode(_ data: Data) throws -> Any? {
        guard let bytes = try BinaryMessageCodec().decode(data) as? Data else {
            return nil
        }
        guard let text = String(data: bytes, encoding: .utf8) else {
            throw invalidMessage()
        }
        return text
    }
}

private func invalidMessage() -> ChannelError {
    ChannelError("serialization_error", "Invalid message value")
}

private final class MessageCancellation: NSObject, CancellationHandler {
    let action: () -> Void
    init(_ action: @escaping () -> Void) {
        self.action = action
    }
    func onCancel() {
        action()
    }
}

public final class MessageResult {
    private let context: RequestContext
    private let codec: any MessageCodec
    init(_ context: RequestContext, codec: any MessageCodec) {
        self.context = context
        self.codec = codec
    }
    public func onCancel(_ action: @escaping () -> Void) {
        context.onCancelled(MessageCancellation(action))
    }
    public func failure() {
        context.complete(2, payload: Data())
    }

    public func success(_ value: Any?) {
        do {
            context.complete(0, payload: try codec.encode(value))
        } catch {
            context.complete(2, payload: Data())
        }
    }
}

private final class MessageRequest: NSObject, RequestHandler {
    let codec: any MessageCodec
    let handler: (Any?, MessageResult) -> Void
    init(_ codec: any MessageCodec, _ handler: @escaping (Any?, MessageResult) -> Void) {
        self.codec = codec
        self.handler = handler
    }
    func receiveRequest(_ channel: String, payload: Data, context: RequestContext) {
        guard !context.isCancelled else {
            return
        }
        do {
            handler(try codec.decode(payload), MessageResult(context, codec: codec))
        } catch {
            context.complete(2, payload: Data())
        }
    }
}

private final class MessageReply: NSObject, Reply {
    let codec: any MessageCodec
    let completion: (Any?, ChannelError?) -> Void
    init(_ codec: any MessageCodec, _ completion: @escaping (Any?, ChannelError?) -> Void) {
        self.codec = codec
        self.completion = completion
    }
    func complete(_ status: Int32, payload: Data) {
        guard status == 0 else {
            completion(
                nil,
                ChannelError(
                    status == 1
                        ? "channel_not_found"
                        : status == 3 ? "disposed" : status == 4 ? "cancelled" : "transport_error",
                    "Managed message failed"))
            return
        }
        do {
            completion(try codec.decode(payload), nil)
        } catch {
            completion(nil, invalidMessage())
        }
    }
}

public final class BasicMessageChannel {
    private let name: String
    private let bridge: NativeBridge
    private let codec: any MessageCodec
    public init(_ name: String, bridge: NativeBridge, codec: any MessageCodec = JsonMessageCodec()) {
        self.name = name
        self.bridge = bridge
        self.codec = codec
    }
    public func setHandler(_ handler: @escaping (Any?, MessageResult) -> Void) -> Bool {
        bridge.registerRequest(name, handler: MessageRequest(codec, handler))
    }
    @discardableResult public func send(_ value: Any?, completion: @escaping (Any?, ChannelError?) -> Void) throws
        -> String
    {
        let id = UUID().uuidString
        bridge.invokeManagedRequest(
            id, channel: name, payload: try codec.encode(value), reply: MessageReply(codec, completion))
        return id
    }
    public func cancel(_ requestId: String) {
        bridge.cancel(requestId)
    }
}
