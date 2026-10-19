#if os(macOS)
  import Foundation

  // ABI v1 transports the same opaque envelopes as the .NET Apple binding.
  // Callback kinds: 0 reply, 1 incoming request, 2 incoming cancellation, 3 closed.
  public typealias DesktopCallback =
    @convention(c) (
      Int32, UnsafePointer<CChar>?, UnsafePointer<CChar>?, Int32, UnsafeRawPointer?, Int32
    ) -> Void

  private final class DesktopReply: NSObject, Reply {
    let callback: (Int32, Data) -> Void
    init(_ callback: @escaping (Int32, Data) -> Void) { self.callback = callback }
    func complete(_ status: Int32, payload: Data) { callback(status, payload) }
  }

  private final class DesktopCancel: NSObject, CancellationHandler {
    let callback: () -> Void
    init(_ callback: @escaping () -> Void) { self.callback = callback }
    func onCancel() { callback() }
  }

  private final class DesktopBridge: NSObject, RequestHandler {
    let bridge = NativeBridge()
    let callback: DesktopCallback
    let callbackLock = NSLock()
    let callbackQueue = DispatchQueue(label: "dev.nativechannels.desktop.callbacks")
    var callbacksClosed = false
    let lock = NSLock()
    var incoming: [String: RequestContext] = [:]

    init(_ callback: @escaping DesktopCallback) {
      self.callback = callback
      super.init()
      bridge.setRequestReceiver(self)
    }

    func emit(
      _ kind: Int32, _ id: String = "", _ channel: String = "", _ status: Int32 = 0,
      _ data: Data = Data()
    ) {
      // Serialize all deliveries and make closed the final callback, even when a
      // native completion races shutdown. No native lock is held across managed code.
      callbackLock.lock()
      defer { callbackLock.unlock() }
      guard !callbacksClosed else { return }
      if kind == 3 { callbacksClosed = true }
      callbackQueue.async { [self] in
        id.withCString { idPointer in
          channel.withCString { channelPointer in
            data.withUnsafeBytes { bytes in
              callback(
                kind, idPointer, channelPointer, status, bytes.baseAddress, Int32(data.count))
            }
          }
        }
      }
    }

    func receiveRequest(_ channel: String, payload: Data, context: RequestContext) {
      // Swift UUID strings are uppercase; .NET formats Guid strings in lowercase.
      let id = context.requestId.lowercased()
      lock.lock()
      incoming[id] = context
      lock.unlock()
      emit(1, id, channel, 0, payload)
      // Enqueue delivery before cancellation, including an already-cancelled context.
      context.onCancelled(DesktopCancel { [weak self] in self?.emit(2, id) })
    }

    func complete(_ id: String, _ status: Int32, _ payload: Data) {
      lock.lock()
      let context = incoming.removeValue(forKey: id)
      lock.unlock()
      context?.complete(status, payload: payload)
    }
  }

  private func desktop(_ pointer: UnsafeMutableRawPointer) -> DesktopBridge {
    Unmanaged<DesktopBridge>.fromOpaque(pointer).takeUnretainedValue()
  }

  @_cdecl("nc_apple_version") public func desktopVersion() -> Int32 { 1 }

  @_cdecl("nc_apple_create")
  public func desktopCreate(_ callback: @escaping DesktopCallback) -> UnsafeMutableRawPointer {
    Unmanaged.passRetained(DesktopBridge(callback)).toOpaque()
  }

  @_cdecl("nc_apple_load_module")
  public func desktopLoadModule(_ pointer: UnsafeMutableRawPointer, _ name: UnsafePointer<CChar>)
    -> Int32
  {
    desktop(pointer).bridge.loadModule(String(cString: name)) == nil ? 0 : 1
  }

  @_cdecl("nc_apple_send")
  public func desktopSend(
    _ pointer: UnsafeMutableRawPointer, _ id: UnsafePointer<CChar>,
    _ channel: UnsafePointer<CChar>, _ bytes: UnsafeRawPointer?, _ count: Int32
  ) {
    let owner = desktop(pointer)
    let identity = String(cString: id)
    guard count >= 0, count <= 16 * 1024 * 1024, count == 0 || bytes != nil else {
      owner.emit(0, identity, "", 2)
      return
    }
    let payload = count == 0 ? Data() : Data(bytes: bytes!, count: Int(count))
    owner.bridge.sendRequest(
      identity, channel: String(cString: channel), payload: payload,
      reply: DesktopReply { [owner] status, data in owner.emit(0, identity, "", status, data) })
  }

  @_cdecl("nc_apple_cancel")
  public func desktopCancel(_ pointer: UnsafeMutableRawPointer, _ id: UnsafePointer<CChar>) {
    desktop(pointer).bridge.cancel(String(cString: id))
  }

  @_cdecl("nc_apple_complete")
  public func desktopComplete(
    _ pointer: UnsafeMutableRawPointer, _ id: UnsafePointer<CChar>,
    _ status: Int32, _ bytes: UnsafeRawPointer?, _ count: Int32
  ) {
    guard count >= 0, count <= 16 * 1024 * 1024, count == 0 || bytes != nil else { return }
    desktop(pointer).complete(
      String(cString: id), status,
      count == 0 ? Data() : Data(bytes: bytes!, count: Int(count)))
  }

  @_cdecl("nc_apple_close")
  public func desktopClose(_ pointer: UnsafeMutableRawPointer) {
    let owner = desktop(pointer)
    owner.bridge.closeAsync(
      DesktopReply { [owner] _, _ in
        owner.lock.lock()
        owner.incoming.removeAll()
        owner.lock.unlock()
        owner.emit(3)
      })
  }

  // The caller waits for callback kind 3 before releasing the owner or managed callback.
  @_cdecl("nc_apple_destroy")
  public func desktopDestroy(_ pointer: UnsafeMutableRawPointer) {
    Unmanaged<DesktopBridge>.fromOpaque(pointer).release()
  }
#endif
