import CoreFoundation
import Foundation

public struct ChannelError: Error {
    public let code: String
    public let message: String
    public let details: Any?
    public init(_ code: String, _ message: String, details: Any? = nil) {
        self.code = code
        self.message = message
        self.details = details
    }
    public static let notImplemented = ChannelError("not_implemented", "Method is not implemented")
}

@objc(MCMethodCall)
public final class MethodCall: NSObject {
    @objc public let method: String
    @objc public let arguments: Any?
    public init(_ method: String, arguments: Any?) {
        self.method = method
        self.arguments = arguments
    }

    public func argument(_ name: String) throws -> Any? {
        guard let map = arguments as? [String: Any], let value = map[name] else {
            throw ChannelError("invalid_arguments", "Missing argument: \(name)")
        }
        return value is NSNull ? nil : value
    }
}

private final class ClosureCancel: NSObject, CancellationHandler {
    let action: () -> Void
    init(_ action: @escaping () -> Void) {
        self.action = action
    }

    func onCancel() {
        action()
    }
}

@objc(MCMethodResult)
public final class MethodResult: NSObject {
    let context: RequestContext
    init(_ context: RequestContext) {
        self.context = context
    }
    @objc public var isCancelled: Bool {
        context.isCancelled
    }

    @objc public func onCancel(_ action: @escaping () -> Void) {
        context.onCancelled(ClosureCancel(action))
    }

    @objc public func success(_ value: Any?) {
        do {
            context.complete(0, payload: try JsonMethodCodec.success(value))
        } catch {
            failure("serialization_error", message: "Cannot encode native result", details: nil)
        }
    }

    @objc public func failure(_ code: String, message: String, details: Any?) {
        do {
            context.complete(0, payload: try JsonMethodCodec.failure(ChannelError(code, message, details: details)))
        } catch {
            context.complete(2, payload: Data())
        }
    }

    @objc public func notImplemented() {
        context.complete(0, payload: Data("{\"v\":1,\"status\":\"not_implemented\"}".utf8))
    }
}

private final class ClosureRequest: NSObject, RequestHandler {
    let handler: (MethodCall, MethodResult) -> Void
    let mainThread: Bool
    init(_ mainThread: Bool, _ handler: @escaping (MethodCall, MethodResult) -> Void) {
        self.mainThread = mainThread
        self.handler = handler
    }

    func receiveRequest(_ channel: String, payload: Data, context: RequestContext) {
        let work = {
            [self] in
            guard !context.isCancelled else {
                return
            }
            let result = MethodResult(context)
            do {
                handler(try JsonMethodCodec.call(payload), result)
            } catch {
                result.failure("serialization_error", message: "Invalid method call", details: nil)
            }
        }
        if mainThread {
            DispatchQueue.main.async(execute: work)
        } else {
            work()
        }
    }
}

private final class ClosureReply: NSObject, Reply {
    let completion: (Any?, ChannelError?) -> Void
    init(_ completion: @escaping (Any?, ChannelError?) -> Void) {
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
                    "Managed request failed"))
            return
        }
        do {
            completion(try JsonMethodCodec.result(payload), nil)
        } catch let error as ChannelError {
            completion(nil, error)
        } catch {
            completion(nil, ChannelError("serialization_error", "Invalid managed response"))
        }
    }
}

@objc(MCMethodChannel)
public final class MethodChannel: NSObject {
    private let name: String
    private let bridge: NativeBridge
    @objc public init(_ name: String, bridge: NativeBridge) {
        self.name = name
        self.bridge = bridge
    }

    @objc public func setHandler(onMainThread: Bool, handler: @escaping (MethodCall, MethodResult) -> Void) -> Bool {
        bridge.registerRequest(name, handler: ClosureRequest(onMainThread, handler))
    }

    public func setAsyncHandler(handler: @escaping (MethodCall) async throws -> Any?) -> Bool {
        setHandler(onMainThread: false) {
            call, result in
            let task = Task {
                do {
                    result.success(try await handler(call))
                } catch let error as ChannelError {
                    if error.code == "not_implemented" {
                        result.notImplemented()
                    } else {
                        result.failure(error.code, message: error.message, details: error.details)
                    }
                } catch {
                    result.failure("native_error", message: "Native handler failed", details: nil)
                }
            }
            result.onCancel {
                task.cancel()
            }
        }
    }

    @discardableResult public func invoke(
        _ method: String, arguments: Any? = nil, completion: @escaping (Any?, ChannelError?) -> Void
    ) throws -> String {
        let id = UUID().uuidString
        bridge.invokeManagedRequest(
            id, channel: name, payload: try JsonMethodCodec.encodeCall(method, arguments: arguments),
            reply: ClosureReply(completion))
        return id
    }

    @objc public func cancel(_ requestId: String) {
        bridge.cancel(requestId)
    }
}

public enum JsonMethodCodec {
    private static func envelope(_ data: Data) throws -> [String: Any] {
        guard data.count <= 16 * 1024 * 1024,
            let root = try JSONSerialization.jsonObject(with: data) as? [String: Any],
            let version = root["v"] as? NSNumber, CFGetTypeID(version) != CFBooleanGetTypeID(), version.doubleValue == 1
        else {
            throw ChannelError("serialization_error", "Invalid protocol envelope")
        }
        return root
    }

    public static func call(_ data: Data) throws -> MethodCall {
        let root = try envelope(data)
        guard let method = root["method"] as? String, !method.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty,
            method.utf8.count <= 256, method.rangeOfCharacter(from: .controlCharacters) == nil,
            let args = root["arguments"]
        else {
            throw ChannelError("serialization_error", "Invalid method name")
        }
        return MethodCall(method, arguments: try decodeValue(args))
    }

    public static func encodeCall(_ method: String, arguments: Any?) throws -> Data {
        try JSONSerialization.data(withJSONObject: ["v": 1, "method": method, "arguments": encodeValue(arguments)])
    }

    public static func success(_ value: Any?) throws -> Data {
        try JSONSerialization.data(withJSONObject: ["v": 1, "status": "success", "value": encodeValue(value)])
    }

    public static func failure(_ error: ChannelError) throws -> Data {
        try JSONSerialization.data(withJSONObject: [
            "v": 1, "status": "error", "code": error.code,
            "message": error.message, "details": encodeValue(error.details), "platform": "apple",
        ])
    }

    public static func result(_ data: Data) throws -> Any? {
        let root = try envelope(data)
        switch root["status"] as? String {
        case "success":
            guard let value = root["value"] else {
                throw ChannelError("serialization_error", "Missing value")
            }
            return try decodeValue(value)
        case "not_implemented": throw ChannelError.notImplemented
        case "error":
            throw ChannelError(
                root["code"] as? String ?? "native_error", root["message"] as? String ?? "Native error",
                details: try decodeValue(root["details"] ?? ["n"]))
        default: throw ChannelError("serialization_error", "Unknown result status")
        }
    }

    public static func encodeValue(_ value: Any?, depth: Int = 0) throws -> [Any] {
        guard depth <= 64 else {
            throw ChannelError("serialization_error", "Value nesting limit exceeded")
        }
        guard let value, !(value is NSNull) else {
            return ["n"]
        }
        if let text = value as? String {
            return ["s", text]
        }
        if let bytes = value as? Data {
            return ["bytes", bytes.base64EncodedString()]
        }
        if type(of: value) == Int64.self {
            return ["i64", String(value as! Int64)]
        }
        if type(of: value) == Int32.self {
            return ["i32", value as! Int32]
        }
        if type(of: value) == UInt64.self {
            throw ChannelError("serialization_error", "Unsigned Int64 is unsupported")
        }
        if let number = value as? NSNumber {
            if CFGetTypeID(number) == CFBooleanGetTypeID() {
                return ["b", number.boolValue]
            }
            let typeCode = String(cString: number.objCType)
            if typeCode == "f" || typeCode == "d" {
                guard number.doubleValue.isFinite else {
                    throw ChannelError("serialization_error", "Non-finite value")
                }
                return ["f64", number.doubleValue]
            }
            if typeCode == "Q" && number.uint64Value > UInt64(Int64.max) {
                throw ChannelError("serialization_error", "Integer is out of range")
            }
            return ["i64", String(number.int64Value)]
        }
        if let list = value as? [Any] {
            return [
                "list",
                try list.map {
                    try encodeValue($0, depth: depth + 1)
                },
            ]
        }
        if let map = value as? [String: Any] {
            return [
                "map",
                try map.mapValues {
                    try encodeValue($0, depth: depth + 1)
                },
            ]
        }
        throw ChannelError("serialization_error", "Unsupported value type")
    }

    public static func decodeValue(_ value: Any, depth: Int = 0) throws -> Any? {
        guard depth <= 64, let array = value as? [Any], let tag = array.first as? String,
            array.count == (tag == "n" ? 1 : 2)
        else {
            throw ChannelError("serialization_error", "Invalid tagged value")
        }
        switch tag {
        case "n": return nil
        case "b":
            if let n = array[1] as? NSNumber, CFGetTypeID(n) == CFBooleanGetTypeID() {
                return n.boolValue
            }
        case "s":
            if let text = array[1] as? String {
                return text
            }
        case "i32":
            if let n = array[1] as? NSNumber, CFGetTypeID(n) != CFBooleanGetTypeID(),
                n.doubleValue == Double(n.int32Value)
            {
                return n.int32Value
            }
        case "i64":
            if let text = array[1] as? String, let n = Int64(text) {
                return n
            }
        case "f64":
            if let n = array[1] as? NSNumber, CFGetTypeID(n) != CFBooleanGetTypeID(), n.doubleValue.isFinite {
                return n.doubleValue
            }
        case "bytes":
            if let text = array[1] as? String, let data = Data(base64Encoded: text) {
                return data
            }
        case "list":
            if let list = array[1] as? [Any] {
                return try list.map {
                    try decodeValue($0, depth: depth + 1) ?? NSNull()
                }
            }
        case "map":
            if let map = array[1] as? [String: Any] {
                return try map.mapValues {
                    try decodeValue($0, depth: depth + 1) ?? NSNull()
                }
            }
        default: break
        }
        throw ChannelError("serialization_error", "Invalid tagged value payload")
    }
}
