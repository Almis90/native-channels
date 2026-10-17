#if os(iOS) || os(macOS)
    import Foundation
    #if os(macOS)
        import AppKit
        public typealias PlatformView = NSView
    #else
        import UIKit
        public typealias PlatformView = UIView
    #endif

    @objc(MCNativeViewInstance)
    public protocol NativeViewInstance: AnyObject {
        var view: PlatformView { get }
        func close()
    }

    @objc(MCNativeViewFactory)
    public protocol NativeViewFactory: AnyObject {
        init()
        func createView(_ bridge: NativeBridge, instanceId: String, channelName: String, arguments: Data) throws
            -> NativeViewInstance
    }

    /// Generic binding surface. The managed owner gives each view its own bridge and closes it on detach.
    @objc(MCNativeViewHost)
    public final class NativeViewHost: NSObject {
        private var instance: NativeViewInstance?
        private init(_ instance: NativeViewInstance) { self.instance = instance }

        @objc(create:bridge:instanceId:channelName:arguments:error:)
        public static func create(
            _ className: String, bridge: NativeBridge, instanceId: String, channelName: String, arguments: Data
        ) throws -> NativeViewHost {
            guard Thread.isMainThread else { throw failure("Native views require the UI thread") }
            guard arguments.count <= 16 * 1024 * 1024 else { throw failure("Creation arguments too large") }
            guard let factory = NSClassFromString(className) as? NativeViewFactory.Type else {
                throw failure("Native view factory not found or does not conform to MCNativeViewFactory: \(className)")
            }
            let created = try factory.init().createView(
                bridge, instanceId: instanceId, channelName: channelName, arguments: Data(arguments))
            guard created.view.superview == nil else {
                created.close()
                throw failure("A view factory must return a new, unattached view")
            }
            return NativeViewHost(created)
        }

        @objc public var view: PlatformView? {
            precondition(Thread.isMainThread, "Native views require the UI thread")
            return instance?.view
        }

        @objc public func close() {
            precondition(Thread.isMainThread, "Native views require the UI thread")
            let old = instance
            instance = nil
            old?.close()
        }

        private static func failure(_ message: String) -> NSError {
            NSError(domain: "NativeChannels.NativeView", code: 1, userInfo: [NSLocalizedDescriptionKey: message])
        }
    }
#endif
