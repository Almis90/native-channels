package dev.nativechannels

import java.util.ArrayDeque

/** One sink and one cancellation registration per incoming listen request. */
class EventChannel(private val name: String, private val bridge: NativeBridge) {
    fun setStreamHandler(handler: (Any?, EventSink) -> Unit) {
        MethodChannel(name, bridge).setHandler { call, result ->
            if (call.method != "listen") {
                result.notImplemented()
                return@setHandler
            }
            val endpoint = call.argument("events") as? String
            require(endpoint != null && endpoint.matches(Regex("__maui[.]events/[0-9a-fA-F]{32}")))
            val sink = EventSink(MethodChannel(endpoint, bridge), result)
            try {
                handler(call.argument("arguments"), sink)
            } catch (error: ChannelError) {
                sink.error(error.code, error.message, error.details)
            } catch (_: Exception) {
                sink.error("native_error", "Native stream handler failed")
            }
        }
    }
}

/** Up to 64 queued events plus one in flight. Overflow fails the subscription explicitly. */
class EventSink
internal constructor(private val channel: MethodChannel, private val result: MethodResult) {
    private class Item(val value: Any?) {
        var id: String? = null
    }

    private val gate = Any()
    private val queue = ArrayDeque<Item>()
    private var sending = false
    private var closed = false
    private var ending = false
    private var failure: ChannelError? = null
    private var current: Item? = null
    private var cleanup: (() -> Unit)? = null
    private var cleanupRegistered = false

    init {
        result.onCancel { finish(null, cancelled = true) }
    }

    /** Runs exactly once on completion, error, cancellation, or bridge disposal. */
    fun onTermination(action: () -> Unit) {
        val run =
            synchronized(gate) {
                check(!cleanupRegistered) { "Cleanup is already registered" }
                cleanupRegistered = true
                if (closed) true
                else {
                    cleanup = action
                    false
                }
            }
        if (run) action()
    }

    fun success(value: Any?) {
        val overflow =
            synchronized(gate) {
                if (closed || ending) return
                if (queue.size == 64) true
                else {
                    queue.addLast(Item(value))
                    false
                }
            }
        if (overflow)
            finish(ChannelError("event_overflow", "Native event buffer capacity 64 exceeded"))
        else pump()
    }

    fun endOfStream() = end(null)

    fun error(code: String, message: String, details: Any? = null) =
        end(ChannelError(code, message, details))

    private fun end(error: ChannelError?) {
        synchronized(gate) {
            if (closed || ending) return
            ending = true
            failure = error
        }
        pump()
    }

    private fun pump() {
        val next: Item?
        val terminal: ChannelError?
        synchronized(gate) {
            if (closed || sending) return
            next = queue.pollFirst()
            if (next == null && !ending) return
            terminal = failure
            sending = next != null
            current = next
        }
        if (next == null) {
            finish(terminal)
            return
        }
        try {
            val id =
                channel.invoke("event", next.value) { _, error ->
                    synchronized(gate) {
                        if (closed || current !== next) return@invoke
                        sending = false
                        current = null
                    }
                    if (error != null) finish(error) else pump()
                }
            val cancel =
                synchronized(gate) {
                    if (closed) true
                    else {
                        if (current === next) next.id = id
                        false
                    }
                }
            if (cancel) channel.cancel(id)
        } catch (_: Exception) {
            finish(ChannelError("serialization_error", "Cannot encode event"))
        }
    }

    private fun finish(error: ChannelError?, cancelled: Boolean = false) {
        val action: (() -> Unit)?
        val request: String?
        synchronized(gate) {
            if (closed) return
            closed = true
            queue.clear()
            action = cleanup
            cleanup = null
            request = current?.id
            current = null
        }
        if (request != null) channel.cancel(request)
        try {
            action?.invoke()
        } catch (_: Exception) {
            // Cleanup cannot prevent the subscription from reaching its terminal state.
        } finally {
            if (!cancelled) {
                if (error == null) result.success(null)
                else result.error(error.code, error.message, error.details)
            }
        }
    }
}
