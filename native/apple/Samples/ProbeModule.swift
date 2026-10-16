import Foundation
import NativeChannelsNative

private final class Handler: NSObject, MessageHandler {
    weak var bridge: NativeBridge?
    init(_ bridge: NativeBridge) {
        self.bridge = bridge
    }

    func receive(_ channel: String, payload: Data, reply: Reply) {
        guard let bridge else {
            reply.complete(3, payload: Data())
            return
        }
        bridge.invokeManaged("managed", payload: payload, reply: ProbeReply(reply))
    }
}

private final class ProbeReply: NSObject, Reply {
    let target: Reply
    init(_ target: Reply) {
        self.target = target
    }

    func complete(_ status: Int32, payload: Data) {
        target.complete(status, payload: status == 0 ? Data("Swift:".utf8) + payload : payload)
    }
}

@objc(MCProbeModule)
public final class ProbeModule: NSObject, NativeModule {
    public required override init() {
        super.init()
    }

    public func register(with bridge: NativeBridge) {
        precondition(bridge.register("probe", handler: Handler(bridge)))
    }
}
