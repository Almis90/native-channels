package dev.nativechannels.pdf

import android.content.Context
import android.graphics.Bitmap
import android.graphics.Color
import android.graphics.pdf.PdfRenderer
import android.os.Handler
import android.os.Looper
import android.os.ParcelFileDescriptor
import dev.nativechannels.*
import java.io.File
import java.util.concurrent.Executors

/** The only native entry point; individual PDF operations remain channel messages. */
class PdfViewFactory : NativeViewFactory {
    override fun create(
        context: Context,
        bridge: NativeBridge,
        instanceId: String,
        channelName: String,
        arguments: ByteArray,
    ): NativeViewInstance = PdfInstance(context, bridge, instanceId, channelName)
}

private class PdfInstance(
    context: Context,
    bridge: NativeBridge,
    private val instanceId: String,
    channelName: String,
) : NativeViewInstance {
    private val canvas = PdfCanvas(context)
    private val channel = MethodChannel(channelName, bridge)
    private val main = Handler(Looper.getMainLooper())
    // Renderer and file descriptors are owned exclusively by this queue, including cleanup.
    private val worker = Executors.newSingleThreadExecutor()
    private var renderer: PdfRenderer? = null
    @Volatile private var closed = false
    private var busy = false
    private var page = 0
    private var pageCount = 0
    private var zoom = 1.0

    init {
        canvas.changePage = { delta ->
            if (!busy && page + delta in 1..pageCount) render(page + delta, null)
        }
        canvas.changeZoom = { value ->
            if (!closed && pageCount > 0) {
                zoom = value
                notifyState()
            }
        }
        channel.setHandler(onMainThread = true) { call, result ->
            if (closed) result.error("view_disposed", "The PDF view is detached")
            else
                when (call.method) {
                    "open" -> {
                        val path =
                            call.arguments as? String
                                ?: throw ChannelError(
                                    "invalid_arguments",
                                    "Expected a local PDF path",
                                )
                        if (busy)
                            result.error("pdf_busy", "Wait for the current document operation")
                        else open(path, result)
                    }
                    "goToPage" -> {
                        val target =
                            call.arguments as? Int
                                ?: throw ChannelError(
                                    "invalid_arguments",
                                    "Expected a one-based page number",
                                )
                        if (target !in 1..pageCount)
                            result.error("invalid_page", "Page is outside the document")
                        else if (busy)
                            result.error("pdf_busy", "Wait for the current document operation")
                        else render(target, result)
                    }
                    "setZoom" -> {
                        val value =
                            call.arguments as? Double
                                ?: throw ChannelError("invalid_arguments", "Expected a zoom factor")
                        if (!value.isFinite() || value !in 1.0..4.0)
                            result.error("invalid_zoom", "Zoom must be between 1 and 4")
                        else if (pageCount == 0)
                            result.error("document_not_open", "Open a document first")
                        else {
                            zoom = value
                            canvas.setZoom(value)
                            notifyState()
                            result.success(state())
                        }
                    }
                    "getState" -> result.success(state())
                    else -> result.notImplemented()
                }
        }
    }

    private fun open(path: String, result: MethodResult) {
        busy = true
        worker.execute {
            var candidate: PdfRenderer? = null
            try {
                val descriptor =
                    ParcelFileDescriptor.open(File(path), ParcelFileDescriptor.MODE_READ_ONLY)
                candidate =
                    try {
                        PdfRenderer(descriptor)
                    } catch (error: Exception) {
                        descriptor.close()
                        throw error
                    }
                if (candidate.pageCount == 0) throw IllegalArgumentException("Empty PDF")
                val bitmap = renderPage(candidate, 1)
                val count = candidate.pageCount
                renderer?.close()
                renderer = candidate
                candidate = null
                main.post {
                    busy = false
                    if (closed) {
                        bitmap.recycle()
                        return@post
                    }
                    pageCount = count
                    page = 1
                    zoom = 1.0
                    canvas.show(bitmap, zoom)
                    notifyState()
                    result.success(state())
                }
            } catch (_: Exception) {
                candidate?.close()
                main.post {
                    busy = false
                    if (!closed)
                        result.error(
                            "pdf_open_failed",
                            "Cannot open this PDF; use a readable, unencrypted local file",
                        )
                }
            }
        }
    }

    private fun render(target: Int, result: MethodResult?) {
        busy = true
        worker.execute {
            try {
                val bitmap = renderPage(checkNotNull(renderer), target)
                main.post {
                    busy = false
                    if (closed) {
                        bitmap.recycle()
                        return@post
                    }
                    page = target
                    canvas.show(bitmap, zoom)
                    notifyState()
                    result?.success(state())
                }
            } catch (_: Exception) {
                main.post {
                    busy = false
                    if (!closed) result?.error("pdf_render_failed", "Unable to render this page")
                }
            }
        }
    }

    private fun renderPage(document: PdfRenderer, number: Int): Bitmap =
        document.openPage(number - 1).use { pdfPage ->
            // At most one displayed page, at most 1600 pixels on either edge (~10 MiB).
            val scale = 1600.0 / maxOf(pdfPage.width, pdfPage.height)
            val bitmap =
                Bitmap.createBitmap(
                    maxOf(1, (pdfPage.width * scale).toInt()),
                    maxOf(1, (pdfPage.height * scale).toInt()),
                    Bitmap.Config.ARGB_8888,
                )
            try {
                bitmap.eraseColor(Color.WHITE)
                pdfPage.render(bitmap, null, null, PdfRenderer.Page.RENDER_MODE_FOR_DISPLAY)
                bitmap
            } catch (error: Exception) {
                bitmap.recycle()
                throw error
            }
        }

    private fun state() =
        mapOf("instanceId" to instanceId, "page" to page, "pageCount" to pageCount, "zoom" to zoom)

    private fun notifyState() {
        if (!closed) channel.invoke("stateChanged", state()) { _, _ -> }
    }

    override fun getView(): android.view.View = canvas

    override fun close() {
        check(Looper.myLooper() == Looper.getMainLooper())
        if (closed) return
        closed = true
        canvas.clear()
        // Queued after any in-flight render; never close a renderer while it is in use.
        worker.execute {
            renderer?.close()
            renderer = null
        }
        worker.shutdown()
    }
}
