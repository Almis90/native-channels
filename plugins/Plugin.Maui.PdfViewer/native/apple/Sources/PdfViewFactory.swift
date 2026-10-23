import Foundation
#if os(macOS)
    import AppKit
#else
    import UIKit
#endif
import PDFKit
import NativeChannelsNative

@objc(MCPdfViewFactory)
public final class PdfViewFactory: NSObject, NativeViewFactory {
    public required override init() { super.init() }
    public func createView(_ bridge: NativeBridge, instanceId: String, channelName: String, arguments: Data) throws
        -> NativeViewInstance
    {
        PdfInstance(bridge, instanceId, channelName)
    }
}

private final class DocumentView: PDFView {
    var resized: (() -> Void)?
    private var previousSize = CGSize.zero
    #if os(macOS)
        override func layout() { super.layout(); didLayout() }
    #else
        override func layoutSubviews() { super.layoutSubviews(); didLayout() }
    #endif
    private func didLayout() {
        if bounds.size != previousSize && bounds.width > 0 && bounds.height > 0 {
            previousSize = bounds.size
            resized?()
        }
    }
}

private final class PdfInstance: NSObject, NativeViewInstance {
    private let pdf = DocumentView()
    private let channel: MethodChannel
    private let instanceId: String
    private let loader = DispatchQueue(label: "dev.nativechannels.pdf.load")
    private var observers: [NSObjectProtocol] = []
    private var closed = false
    private var busy = false
    private var notificationQueued = false
    private var fitScale: CGFloat = 1
    #if os(macOS)
        var view: NSView { pdf }
    #else
        var view: UIView { pdf }
    #endif

    init(_ bridge: NativeBridge, _ instanceId: String, _ channelName: String) {
        self.channel = MethodChannel(channelName, bridge: bridge)
        self.instanceId = instanceId
        super.init()
        #if os(macOS)
            pdf.backgroundColor = NSColor(red: 0.886, green: 0.91, blue: 0.94, alpha: 1)
        #else
            pdf.backgroundColor = UIColor(red: 0.886, green: 0.91, blue: 0.94, alpha: 1)
        #endif
        pdf.displayMode = .singlePage
        pdf.displayDirection = .horizontal
        #if !os(macOS)
            pdf.usePageViewController(true)
        #endif
        pdf.resized = { [weak self] in self?.fit() }
        for name in [Notification.Name.PDFViewPageChanged, Notification.Name.PDFViewScaleChanged] {
            observers.append(
                NotificationCenter.default.addObserver(forName: name, object: pdf, queue: .main) {
                    [weak self] _ in self?.notifyState()
                })
        }
        precondition(
            channel.setHandler(onMainThread: true) { [weak self] call, result in
                guard let self, !self.closed else {
                    result.failure("view_disposed", message: "The PDF view is detached", details: nil)
                    return
                }
                self.handle(call, result)
            })
    }

    private func handle(_ call: MethodCall, _ result: MethodResult) {
        switch call.method {
        case "open":
            guard let path = call.arguments as? String, path.hasPrefix("/") else {
                result.failure("invalid_arguments", message: "Expected an absolute local PDF path", details: nil)
                return
            }
            guard !busy else {
                result.failure("pdf_busy", message: "Wait for the current document operation", details: nil)
                return
            }
            busy = true
            loader.async { [weak self] in
                let document = PDFDocument(url: URL(fileURLWithPath: path))
                DispatchQueue.main.async { [weak self] in
                    guard let self, !closed else { return }
                    busy = false
                    guard let document, !document.isLocked, document.pageCount > 0 else {
                        result.failure(
                            "pdf_open_failed", message: "Cannot open this PDF; use a readable, unencrypted local file",
                            details: nil)
                        return
                    }
                    pdf.document = document
                    fit()
                    notifyState()
                    result.success(state())
                }
            }
        case "goToPage":
            guard let number = call.arguments as? Int32 else {
                result.failure("invalid_arguments", message: "Expected a one-based page number", details: nil)
                return
            }
            guard let document = pdf.document, number > 0, Int(number) <= document.pageCount,
                let page = document.page(at: Int(number) - 1)
            else {
                result.failure("invalid_page", message: "Page is outside the document", details: nil)
                return
            }
            pdf.go(to: page)
            notifyState()
            result.success(state())
        case "setZoom":
            guard let number = call.arguments as? Double, number.isFinite, (1...4).contains(number) else {
                result.failure("invalid_zoom", message: "Zoom must be between 1 and 4", details: nil)
                return
            }
            guard pdf.document != nil else {
                result.failure("document_not_open", message: "Open a document first", details: nil)
                return
            }
            pdf.scaleFactor = fitScale * number
            notifyState()
            result.success(state())
        case "getState": result.success(state())
        default: result.notImplemented()
        }
    }

    private func fit() {
        guard !closed, pdf.document != nil else { return }
        // Opening immediately after reconnect can precede the first native layout.
        // PDFKit then reports a zero fit scale. Keep a valid native scale until
        // the resize callback can calculate the document's actual size-to-fit.
        var value = fitScale
        if pdf.bounds.width > 0, pdf.bounds.height > 0 {
            pdf.layoutDocumentView()
            let fitted = pdf.scaleFactorForSizeToFit
            if fitted.isFinite, fitted > 0 { value = fitted }
        }
        fitScale = value
        pdf.minScaleFactor = value
        pdf.maxScaleFactor = value * 4
        pdf.scaleFactor = value
        notifyState()
    }

    private func state() -> [String: Any] {
        var page = 0
        if let document = pdf.document, let current = pdf.currentPage {
            let index = document.index(for: current)
            if index != NSNotFound { page = index + 1 }
        }
        return [
            "instanceId": instanceId, "page": Int32(page),
            "pageCount": Int32(pdf.document?.pageCount ?? 0),
            "zoom": pdf.document == nil ? 1.0 : Double(pdf.scaleFactor / fitScale),
        ]
    }

    private func notifyState() {
        guard !closed, !notificationQueued else { return }
        notificationQueued = true
        // Coalesce PDFKit layout/gesture notifications; never wait on C# on the UI queue.
        DispatchQueue.main.async { [weak self] in
            guard let self, !closed else { return }
            notificationQueued = false
            do { try channel.invoke("stateChanged", arguments: state()) { _, _ in } } catch
            { /* The bridge owns teardown and cancellation. */  }
        }
    }

    func close() {
        precondition(Thread.isMainThread)
        guard !closed else { return }
        closed = true
        observers.forEach { NotificationCenter.default.removeObserver($0) }
        observers.removeAll()
        pdf.resized = nil
        pdf.document = nil
    }
}
