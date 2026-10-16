import Foundation

final class Result: NSObject, Reply {
    let done = DispatchSemaphore(value: 0)
    var status: Int32 = -1
    var value = Data()
    func complete(_ status: Int32, payload: Data) {
        self.status = status
        value = payload
        done.signal()
    }

    func wait() {
        precondition(done.wait(timeout: .now() + 5) == .success, "Reply timed out")
    }
}

final class Managed: NSObject, MessageHandler {
    func receive(_ channel: String, payload: Data, reply: Reply) {
        reply.complete(0, payload: payload + Data("-managed".utf8))
        reply.complete(2, payload: Data("duplicate".utf8))
    }
}

final class Native: NSObject, MessageHandler {
    weak var bridge: NativeBridge?
    init(_ bridge: NativeBridge) {
        self.bridge = bridge
    }

    func receive(_ channel: String, payload: Data, reply: Reply) {
        bridge!.invokeManaged("managed", payload: payload, reply: reply)
    }
}

final class Deferred: NSObject, MessageHandler {
    let entered = DispatchSemaphore(value: 0)
    var reply: Reply?
    func receive(_ channel: String, payload: Data, reply: Reply) {
        self.reply = reply
        entered.signal()
    }
}

let bridge = NativeBridge()
bridge.setReceiver(Managed())
precondition(bridge.register("native", handler: Native(bridge)))
let result = Result()
bridge.send("native", payload: Data("hello".utf8), reply: result)
result.wait()
precondition(result.status == 0 && String(data: result.value, encoding: .utf8) == "hello-managed")
let missing = Result()
bridge.send("unknown", payload: Data(), reply: missing)
missing.wait()
precondition(missing.status == 1)
let deferred = Deferred()
precondition(bridge.register("deferred", handler: deferred))
let interrupted = Result()
bridge.send("deferred", payload: Data(), reply: interrupted)
precondition(deferred.entered.wait(timeout: .now() + 5) == .success)
bridge.close()
interrupted.wait()
precondition(interrupted.status == 3)
deferred.reply?.complete(0, payload: Data("late".utf8))
precondition(interrupted.done.wait(timeout: .now() + 0.05) == .timedOut)
let disposed = Result()
bridge.send("native", payload: Data(), reply: disposed)
disposed.wait()
precondition(disposed.status == 3)
print(
    "PASS Swift native bridge: reentrant callback, duplicate suppression, missing endpoint, pending disposal, late reply"
)

let fixtures =
    try JSONSerialization.jsonObject(with: Data(contentsOf: URL(fileURLWithPath: "tests/fixtures/values.json")))
    as! [String: [[Any]]]
for value in fixtures["valid"]! {
    let decoded = try JsonMethodCodec.decodeValue(value)
    let encoded = try JsonMethodCodec.encodeValue(decoded)
    _ = try JsonMethodCodec.decodeValue(encoded)
}
for value in fixtures["invalid"]! {
    do {
        _ = try JsonMethodCodec.decodeValue(value)
        preconditionFailure("Malformed value accepted: \(value)")
    } catch {
    }
}
let codecNull = try JsonMethodCodec.result(JsonMethodCodec.success(nil))
precondition(codecNull == nil)
do {
    _ = try JsonMethodCodec.result(JsonMethodCodec.failure(ChannelError("expected", "message")))
    preconditionFailure("Error accepted as success")
} catch let error as ChannelError {
    precondition(error.code == "expected")
}
print("PASS Swift shared protocol fixtures and null/error distinction")

final class Cancelled: NSObject, CancellationHandler {
    var called = false
    func onCancel() {
        called = true
    }
}
final class DeferredRequest: NSObject, RequestHandler {
    let entered = DispatchSemaphore(value: 0)
    var context: RequestContext?
    func receiveRequest(_ channel: String, payload: Data, context: RequestContext) {
        self.context = context
        entered.signal()
    }
}
let cancellable = NativeBridge()
let requestHandler = DeferredRequest()
precondition(cancellable.registerRequest("cancel", handler: requestHandler))
let requestId = UUID().uuidString
let first = Result()
cancellable.sendRequest(requestId, channel: "cancel", payload: Data(), reply: first)
precondition(requestHandler.entered.wait(timeout: .now() + 5) == .success)
let duplicate = Result()
cancellable.sendRequest(requestId, channel: "cancel", payload: Data(), reply: duplicate)
duplicate.wait()
precondition(duplicate.status == 2)
let cancellation = Cancelled()
requestHandler.context!.onCancelled(cancellation)
cancellable.cancel(requestId)
first.wait()
precondition(first.status == 4 && cancellation.called)
requestHandler.context!.complete(0, payload: Data())
precondition(first.done.wait(timeout: .now() + 0.05) == .timedOut)
let drain = Result()
cancellable.closeAsync(drain)
drain.wait()
precondition(drain.status == 0)
print("PASS Swift cancellation, duplicate ID isolation, asynchronous drain")
do {
    _ = try JsonMethodCodec.encodeValue(UInt64.max)
    preconditionFailure("Lossy unsigned conversion accepted")
} catch {
}

for codec: any MessageCodec in [JsonMessageCodec(), BinaryMessageCodec(), StringMessageCodec()] {
    let value = try codec.decode(codec.encode(nil))
    precondition(value == nil)
}
let emptyBytes = try BinaryMessageCodec().decode(Data([1])) as! Data
let emptyText = try StringMessageCodec().decode(Data([1])) as! String
let unicode = try StringMessageCodec().decode(StringMessageCodec().encode("€😀")) as! String
precondition(emptyBytes.isEmpty && emptyText.isEmpty && unicode == "€😀")
for malformed in [Data(), Data([0, 1]), Data([2]), Data([1, 255])] {
    do {
        _ = try StringMessageCodec().decode(malformed)
        preconditionFailure("Malformed message accepted")
    } catch {}
}

final class EventReceiver: NSObject, RequestHandler {
    var values: [Any?] = []
    var stall = false
    func receiveRequest(_ channel: String, payload: Data, context: RequestContext) {
        if stall {
            return
        }
        values.append(try! JsonMethodCodec.call(payload).arguments)
        context.complete(0, payload: try! JsonMethodCodec.success(nil))
    }
}
func listen(_ bridge: NativeBridge) throws -> (String, Result) {
    let id = UUID().uuidString
    let result = Result()
    try bridge.sendRequest(
        id, channel: "events",
        payload: JsonMethodCodec.encodeCall(
            "listen",
            arguments: [
                "events": "__maui.events/" + UUID().uuidString.replacingOccurrences(of: "-", with: ""),
                "arguments": NSNull(),
            ]), reply: result)
    return (id, result)
}
let eventsBridge = NativeBridge()
let receiver = EventReceiver()
eventsBridge.setRequestReceiver(receiver)
var cleanups = 0
precondition(
    EventChannel("events", bridge: eventsBridge).setStreamHandler { _, sink in
        sink.onTermination {
            cleanups += 1
        }
        sink.success(nil)
        sink.success(Int32(42))
        sink.endOfStream()
        sink.endOfStream()
    })
let (_, completed) = try listen(eventsBridge)
completed.wait()
let terminal = try JsonMethodCodec.result(completed.value)
precondition(terminal == nil && receiver.values.count == 2 && receiver.values[0] == nil)
precondition(receiver.values[1] as? Int32 == 42 && cleanups == 1)
eventsBridge.close()

let stalledBridge = NativeBridge()
let stalledReceiver = EventReceiver()
stalledReceiver.stall = true
stalledBridge.setRequestReceiver(stalledReceiver)
let ready = DispatchSemaphore(value: 0)
var sinks: [EventSink] = []
precondition(
    EventChannel("events", bridge: stalledBridge).setStreamHandler { _, sink in
        sinks.append(sink)
        ready.signal()
    })
let (cancelId, cancelledStream) = try listen(stalledBridge)
precondition(ready.wait(timeout: .now() + 5) == .success)
stalledBridge.cancel(cancelId)
cancelledStream.wait()
sinks[0].onTermination {
    cleanups += 1
}
precondition(cancelledStream.status == 4 && cleanups == 2)
let (_, overflow) = try listen(stalledBridge)
precondition(ready.wait(timeout: .now() + 5) == .success)
sinks[1].onTermination {
    cleanups += 1
}
for value in 0..<66 {
    sinks[1].success(Int32(value))
}
overflow.wait()
do {
    _ = try JsonMethodCodec.result(overflow.value)
    preconditionFailure("Native overflow accepted")
} catch let error as ChannelError {
    precondition(error.code == "event_overflow")
}
precondition(cleanups == 3)
stalledBridge.close()
precondition(cleanups == 3)
print("PASS Swift message codecs, null events, ordered completion, independent listeners, overflow and cleanup")

// StandardMessageCodec uses the same independent byte fixtures as C# and Kotlin.
func standardFixtureValue(_ value: [Any]) -> Any? {
    let tag = value[0] as! String
    switch tag {
    case "n": return nil
    case "b": return value[1] as! Bool
    case "s": return value[1] as! String
    case "i32": return (value[1] as! NSNumber).int32Value
    case "i64": return Int64(value[1] as! String)!
    case "f64": return (value[1] as! NSNumber).doubleValue
    case "bytes": return Data((value[1] as! [NSNumber]).map { $0.uint8Value })
    case "i32s": return (value[1] as! [NSNumber]).map { $0.int32Value }
    case "i64s": return (value[1] as! [String]).map { Int64($0)! }
    case "f32s": return (value[1] as! [NSNumber]).map { $0.floatValue }
    case "f64s": return (value[1] as! [NSNumber]).map { $0.doubleValue }
    case "list": return (value[1] as! [[Any]]).map { standardFixtureValue($0) ?? NSNull() }
    case "map":
        return StandardMessageMap(
            (value[1] as! [[[Any]]]).map {
                (key: standardFixtureValue($0[0]), value: standardFixtureValue($0[1]))
            })
    default: preconditionFailure("Invalid fixture")
    }
}
func standardHex(_ text: String) -> Data {
    let chars = Array(text)
    return Data(stride(from: 0, to: chars.count, by: 2).map { UInt8(String(chars[$0...($0 + 1)]), radix: 16)! })
}
func standardRejects(_ action: () throws -> Void) {
    do {
        try action()
        preconditionFailure("Expected standard serialization error")
    } catch let error as ChannelError {
        precondition(error.code == "serialization_error")
    } catch { preconditionFailure("Unexpected error: \(error)") }
}
let standard = StandardMessageCodec()
let standardFixtures =
    try JSONSerialization.jsonObject(
        with: Data(contentsOf: URL(fileURLWithPath: "tests/fixtures/standard-values.json"))) as! [String: Any]
for fixture in standardFixtures["valid"] as! [[String: Any]] {
    let bytes = standardHex(fixture["hex"] as! String)
    let encoded = try standard.encode(standardFixtureValue(fixture["value"] as! [Any]))
    precondition(encoded == bytes, "Encode fixture: \(fixture["name"]!)")
    let reencoded = try standard.encode(standard.decode(bytes))
    precondition(reencoded == bytes, "Decode fixture: \(fixture["name"]!)")
    for count in 0..<bytes.count { standardRejects { _ = try standard.decode(Data(bytes.prefix(count))) } }
}
for invalid in standardFixtures["invalid"] as! [String] {
    standardRejects { _ = try standard.decode(standardHex(invalid)) }
}
for count in [253, 254, 65535, 65536] {
    let bytes = Data(repeating: 0, count: count)
    let encoded = try standard.encode(bytes)
    precondition(encoded[1] == (count < 254 ? UInt8(count) : count <= 65535 ? 254 : 255))
    let decoded = try standard.decode(encoded) as! Data
    precondition(decoded == bytes)
    let integers = (0..<count).map { Int32($0) }
    let roundtrip = try standard.decode(standard.encode(integers)) as! [Int32]
    precondition(roundtrip == integers)
}
let standardFloats: [Float] = [.nan, .infinity, -.infinity, -0.0]
let standardFloatResult = try standard.decode(standard.encode(standardFloats)) as! [Float]
precondition(standardFloats.map(\.bitPattern) == standardFloatResult.map(\.bitPattern))
for number: Double in [.nan, .infinity, -.infinity, -0.0] {
    let result = try standard.decode(standard.encode(number)) as! Double
    precondition(result.bitPattern == number.bitPattern)
}
var standardDeep: Any = NSNull()
for _ in 0..<66 { standardDeep = [standardDeep] }
standardRejects { _ = try standard.encode(standardDeep) }
standardRejects { _ = try standard.decode(Data(Array(repeating: [UInt8(12), 1], count: 66).flatMap { $0 } + [0])) }
standardRejects { _ = try standard.decode(Data(repeating: 0, count: 16 * 1024 * 1024 + 1)) }
standardRejects { _ = try standard.encode(Data(repeating: 0, count: 16 * 1024 * 1024)) }
standardRejects { _ = try standard.encode(Date()) }
print(
    "PASS Swift standard codec: shared bytes, malformed/truncated data, length boundaries, typed arrays, IEEE values and limits"
)

standardRejects { _ = try standard.encode(NSDecimalNumber(string: "1.234567890123456789")) }
let standardFoundationCycle = NSMutableArray()
standardFoundationCycle.add(standardFoundationCycle)
standardRejects { _ = try standard.encode(standardFoundationCycle) }
standardFoundationCycle.removeAllObjects()
