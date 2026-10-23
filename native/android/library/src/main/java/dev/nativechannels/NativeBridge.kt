package dev.nativechannels

import java.util.UUID
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.Executors
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicReference

/** Session-scoped generic bridge. No SDK-specific method appears in this ABI. */
class NativeBridge : AutoCloseable {
    private val handlers = ConcurrentHashMap<String, MessageHandler>()
    private val requestHandlers = ConcurrentHashMap<String, RequestHandler>()
    private val queue = Executors.newSingleThreadExecutor()
    private val closed = AtomicBoolean(false)
    private val lifecycle = Any()
    private val pending = ConcurrentHashMap<String, PendingReply>()
    @Volatile private var receiver: MessageHandler? = null
    @Volatile private var requestReceiver: RequestHandler? = null

    fun getProtocolVersion(): Int = 1

    fun setRequestReceiver(handler: RequestHandler?) =
        synchronized(lifecycle) { if (!closed.get()) requestReceiver = handler }

    fun registerRequest(channel: String, handler: RequestHandler) =
        synchronized(lifecycle) {
            validate(channel, byteArrayOf())
            check(!closed.get()) { "Bridge disposed" }
            require(
                !handlers.containsKey(channel) &&
                    requestHandlers.putIfAbsent(channel, handler) == null
            ) {
                "Duplicate channel: $channel"
            }
        }

    fun sendRequest(id: String, channel: String, payload: ByteArray, reply: Reply) =
        dispatch(id, channel, payload, reply, false)

    fun invokeManagedRequest(id: String, channel: String, payload: ByteArray, reply: Reply) =
        dispatch(id, channel, payload, reply, true)

    fun cancel(id: String) {
        pending[id]?.cancel(4)
    }

    fun setReceiver(handler: MessageHandler?) =
        synchronized(lifecycle) { if (!closed.get()) receiver = handler }

    fun register(channel: String, handler: MessageHandler) =
        synchronized(lifecycle) {
            check(!closed.get()) { "Bridge disposed" }
            validate(channel, byteArrayOf())
            require(
                !requestHandlers.containsKey(channel) &&
                    handlers.putIfAbsent(channel, handler) == null
            ) {
                "Channel already registered: $channel"
            }
        }

    fun loadModule(className: String) {
        val module = Class.forName(className).getDeclaredConstructor().newInstance() as NativeModule
        module.register(this)
    }

    fun send(channel: String, payload: ByteArray, reply: Reply) {
        dispatch(UUID.randomUUID().toString(), channel, payload, reply, false)
    }

    fun invokeManaged(channel: String, payload: ByteArray, reply: Reply) {
        dispatch(UUID.randomUUID().toString(), channel, payload, reply, true)
    }

    private fun dispatch(
        id: String,
        channel: String,
        payload: ByteArray,
        reply: Reply,
        toManaged: Boolean,
    ) {
        validate(channel, payload)
        require(id.length == 36 && UUID.fromString(id).toString().equals(id, ignoreCase = true)) {
            "Invalid request ID"
        }
        val once = PendingReply(id, reply) { pending.remove(id, it) }
        val accepted =
            synchronized(lifecycle) {
                if (closed.get()) false
                else {
                    require(pending.putIfAbsent(id, once) == null) { "Duplicate request ID" }
                    true
                }
            }
        if (!accepted) {
            once.complete(3, byteArrayOf())
            return
        }
        val copy = payload.copyOf()
        try {
            queue.execute {
                if (closed.get()) {
                    once.complete(3, byteArrayOf())
                    return@execute
                }
                if (once.isCancelled()) return@execute
                val handler = if (toManaged) receiver else handlers[channel]
                val requestHandler = if (toManaged) requestReceiver else requestHandlers[channel]
                if (handler == null && requestHandler == null) {
                    once.complete(1, byteArrayOf())
                    return@execute
                }
                try {
                    if (requestHandler != null) requestHandler.onRequest(channel, copy, once)
                    else handler!!.onMessage(channel, copy, once)
                } catch (_: Exception) {
                    once.complete(2, "Native handler failed".toByteArray())
                }
            }
        } catch (_: java.util.concurrent.RejectedExecutionException) {
            once.complete(3, byteArrayOf())
        }
    }

    private fun validate(channel: String, payload: ByteArray) {
        require(
            channel.isNotBlank() &&
                channel.toByteArray().size <= 256 &&
                channel.none { it.isISOControl() }
        ) {
            "Invalid channel name"
        }
        require(payload.size <= 16 * 1024 * 1024) { "Payload too large" }
    }

    private class PendingReply(
        private val id: String,
        reply: Reply,
        private val finished: (PendingReply) -> Unit,
    ) : RequestContext {
        private val target = AtomicReference<Reply?>(reply)
        private val cancelled = AtomicBoolean(false)
        private val cancellation = AtomicReference<CancellationHandler?>(null)

        override fun getRequestId(): String = id

        override fun isCancelled(): Boolean = cancelled.get()

        override fun onCancelled(handler: CancellationHandler) {
            cancellation.set(handler)
            if (cancelled.get()) cancellation.getAndSet(null)?.onCancel()
            else if (target.get() == null) cancellation.set(null)
        }

        fun cancel(status: Int) {
            if (!cancelled.compareAndSet(false, true)) return
            try {
                cancellation.getAndSet(null)?.onCancel()
            } finally {
                complete(status, byteArrayOf())
            }
        }

        override fun complete(status: Int, payload: ByteArray) {
            val callback = target.getAndSet(null) ?: return
            cancellation.set(null)
            finished(this)
            if (payload.size > 16 * 1024 * 1024) callback.complete(2, byteArrayOf())
            else callback.complete(status, payload.copyOf())
        }
    }

    private fun closeState() {
        val outstanding =
            synchronized(lifecycle) {
                if (!closed.compareAndSet(false, true)) return
                receiver = null
                requestReceiver = null
                handlers.clear()
                requestHandlers.clear()
                // Replies can complete outside the lifecycle lock. Collection.toList()
                // assumes a size of one remains nonempty until iterator.next().
                // ConcurrentHashMap's array snapshot tolerates concurrent removals.
                pending.values.toTypedArray()
            }
        outstanding.forEach {
            try {
                it.cancel(3)
            } catch (_: Exception) {
                /* Continue releasing every callback. */
            }
        }
    }

    override fun close() {
        closeState()
        queue.shutdown()
    }

    /**
     * Completion runs after all previously queued handler entries, allowing managed peers to be
     * released.
     */
    fun closeAsync(completion: Reply) {
        closeState()
        try {
            queue.execute { completion.complete(0, byteArrayOf()) }
            queue.shutdown()
        } catch (_: java.util.concurrent.RejectedExecutionException) {
            Thread {
                    queue.awaitTermination(
                        Long.MAX_VALUE,
                        java.util.concurrent.TimeUnit.NANOSECONDS,
                    )
                    completion.complete(0, byteArrayOf())
                }
                .start()
        }
    }
}
