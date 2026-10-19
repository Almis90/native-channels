#if os(macOS)
    import Foundation
    import AppKit
    import NativeChannelsNative

    @objc(MCButtonViewFactory)
    public final class ButtonViewFactory: NSObject, NativeViewFactory {
        public required override init() { super.init() }
        public func createView(_ bridge: NativeBridge, instanceId: String, channelName: String, arguments: Data) throws
            -> NativeViewInstance
        {
            let options = try JsonMessageCodec().decode(arguments) as? [String: Any]
            return ButtonInstance(bridge, instanceId, channelName, options?["title"] as? String ?? "Native button")
        }
    }

    private final class ButtonInstance: NSObject, NativeViewInstance {
        private let button = NSButton()
        private let channel: MethodChannel
        private let instanceId: String
        private var title: String
        private var count: Int32 = 0
        private var closed = false
        var view: NSView { button }

        init(_ bridge: NativeBridge, _ instanceId: String, _ channelName: String, _ title: String) {
            self.channel = MethodChannel(channelName, bridge: bridge)
            self.instanceId = instanceId
            self.title = title
            super.init()
            button.bezelStyle = .rounded
            button.target = self
            button.action = #selector(clicked)
            update()
            precondition(
                channel.setHandler(onMainThread: true) { [weak self] call, result in
                    guard let self, !closed else {
                        result.failure("view_disposed", message: "The native view is detached", details: nil)
                        return
                    }
                    precondition(Thread.isMainThread)
                    switch call.method {
                    case "getState":
                        result.success([
                            "instanceId": instanceId, "count": count, "title": self.title, "uiThread": true,
                        ])
                    case "setTitle":
                        guard let value = call.arguments as? String else {
                            result.failure("invalid_arguments", message: "Expected title", details: nil)
                            return
                        }
                        self.title = value
                        update()
                        result.success(nil)
                    case "simulateTap": tap(result)
                    case "wait": break
                    default: result.notImplemented()
                    }
                })
        }
        private func update() { button.title = "\(title) · \(count) taps" }
        @objc private func clicked() { tap(nil) }
        private func tap(_ result: MethodResult?) {
            guard !closed else { return }
            count += 1
            update()
            do {
                try channel.invoke("clicked", arguments: ["instanceId": instanceId, "count": count]) { value, error in
                    if let error {
                        result?.failure(error.code, message: error.message, details: error.details)
                    } else {
                        result?.success(value)
                    }
                }
            } catch {
                result?.failure("callback_failed", message: String(describing: error), details: nil)
            }
        }
        func close() {
            precondition(Thread.isMainThread)
            closed = true
            button.target = nil
            button.action = nil
        }
    }
#endif
