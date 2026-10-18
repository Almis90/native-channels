import Foundation
import Darwin
import NativeChannelsNative

@objc(MCSampleModule)
public final class SampleModule: NSObject, NativeModule {
    public required override init() {
        super.init()
    }

    public func register(with bridge: NativeBridge) {
        let streams = StreamState()
        for (name, codec): (String, any MessageCodec) in [
            ("json", JsonMessageCodec()), ("string", StringMessageCodec()), ("binary", BinaryMessageCodec()),
            ("standard", StandardMessageCodec()),
        ] {
            let messages = BasicMessageChannel("sample.messages.\(name)", bridge: bridge, codec: codec)
            precondition(
                messages.setHandler { value, result in
                    do {
                        try messages.send(value) { reply, error in
                            if error != nil {
                                result.failure()
                            } else {
                                result.success(reply)
                            }
                        }
                    } catch {
                        result.failure()
                    }
                })
        }
        precondition(
            EventChannel("sample.events", bridge: bridge).setStreamHandler { mode, sink in
                let id = streams.start()
                sink.onTermination {
                    streams.stop()
                }
                switch mode as? String {
                case "live": sink.success(id)
                case "error":
                    sink.success(Int32(1));
                    sink.failure("sample_stream_error", message: "Native stream failed", details: Int32(42))
                default:
                    sink.success(nil)
                    sink.success(Int32(42))
                    sink.endOfStream()
                }
            })
        let channel = MethodChannel("sample", bridge: bridge)
        precondition(
            channel.setHandler(onMainThread: false) {
                call, result in
                do {
                    switch call.method {
                    case "activeStreams": result.success(streams.count)
                    case "memory": result.success(try processMemory())
                    case "add":
                        guard let a = try call.argument("a") as? Int32, let b = try call.argument("b") as? Int32 else {
                            throw ChannelError("invalid_arguments", "Expected Int32 arguments")
                        }
                        result.success(a + b)
                    case "device":
                        result.success([
                            "platform": "apple", "name": ProcessInfo.processInfo.operatingSystemVersionString,
                            "id": Int64.max,
                        ])
                    case "echo": result.success(call.arguments)
                    case "null": result.success(nil)
                    case "fail":
                        result.failure("sample_error", message: "Sample native failure", details: ["retryable": false])
                    case "callback":
                        try channel.invoke("getToken") {
                            value, error in
                            if let error {
                                result.failure(error.code, message: error.message, details: error.details)
                            } else {
                                result.success("native:\(value as? String ?? "")")
                            }
                        }
                    case "delay":
                        guard let milliseconds = call.arguments as? Int32, milliseconds >= 0 else {
                            throw ChannelError("invalid_arguments", "Expected delay milliseconds")
                        }
                        let work = DispatchWorkItem {
                            if !result.isCancelled {
                                result.success(milliseconds)
                            }
                        }
                        result.onCancel {
                            work.cancel()
                        }
                        DispatchQueue.global().asyncAfter(
                            deadline: .now() + .milliseconds(Int(milliseconds)), execute: work)
                    default: result.notImplemented()
                    }
                } catch let error as ChannelError {
                    result.failure(error.code, message: error.message, details: error.details)
                } catch {
                    result.failure("native_error", message: "Sample handler failed", details: nil)
                }
            })
        precondition(
            MethodChannel("sample.ui", bridge: bridge).setHandler(onMainThread: true) {
                _, result in
                result.success(Thread.isMainThread)
            })
    }
}

private func processMemory() throws -> [String: Int64] {
    var info = task_vm_info_data_t()
    var count = mach_msg_type_number_t(MemoryLayout<task_vm_info_data_t>.size / MemoryLayout<integer_t>.size)
    let status = withUnsafeMutablePointer(to: &info) { pointer in
        pointer.withMemoryRebound(to: integer_t.self, capacity: Int(count)) {
            task_info(mach_task_self_, task_flavor_t(TASK_VM_INFO), $0, &count)
        }
    }
    guard status == KERN_SUCCESS else {
        throw ChannelError("memory_unavailable", "task_info failed: \(status)")
    }
    return ["physicalFootprint": Int64(info.phys_footprint), "resident": Int64(info.resident_size)]
}

private final class StreamState {
    private let lock = NSLock()
    private var active: Int32 = 0
    private var next: Int32 = 0
    var count: Int32 {
        lock.lock()
        defer {
            lock.unlock()
        }
        return active
    }
    func start() -> Int32 {
        lock.lock()
        defer {
            lock.unlock()
        }
        active += 1
        next += 1
        return next
    }
    func stop() {
        lock.lock()
        defer {
            lock.unlock()
        }
        active -= 1
    }
}
