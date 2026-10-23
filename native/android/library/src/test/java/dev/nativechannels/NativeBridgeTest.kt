package dev.nativechannels

import java.util.concurrent.CompletableFuture
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicInteger
import org.junit.Assert.*
import org.junit.Test

class NativeBridgeTest {
    @Test
    fun closeToleratesLastReplyCompletingDuringSnapshot() {
        val bridge = NativeBridge()
        val entered = CompletableFuture<Reply>()
        val replies = AtomicInteger()
        bridge.register("deferred", MessageHandler { _, _, reply -> entered.complete(reply) })
        bridge.send("deferred", byteArrayOf(), Reply { _, _ -> replies.incrementAndGet() })
        val reply = entered.get(5, TimeUnit.SECONDS)
        val field = NativeBridge::class.java.getDeclaredField("pending")
        field.isAccessible = true
        @Suppress("UNCHECKED_CAST")
        val pending = field.get(bridge) as ConcurrentHashMap<String, Any>
        // Force the exact interleaving seen on Android: size reports one, then
        // the last callback removes itself before snapshot iteration starts.
        val shrinking =
            object : ConcurrentHashMap<String, Any>() {
                override val values: MutableCollection<Any>
                    get() {
                        val live = super.values
                        return object : AbstractMutableCollection<Any>() {
                            override val size: Int
                                get() {
                                    val count = live.size
                                    reply.complete(0, byteArrayOf())
                                    return count
                                }

                            override fun iterator(): MutableIterator<Any> = live.iterator()

                            override fun add(element: Any): Boolean = error("Read-only test view")
                        }
                    }
            }
        shrinking.putAll(pending)
        field.set(bridge, shrinking)
        try {
            val drained = CompletableFuture<Boolean>()
            bridge.closeAsync { _, _ -> drained.complete(true) }
            assertTrue(drained.get(5, TimeUnit.SECONDS))
            assertEquals(1, replies.get())
        } finally {
            bridge.close()
        }
    }

    @Test
    fun cancellationAndDrainReleaseOnce() {
        val bridge = NativeBridge()
        val id = java.util.UUID.randomUUID().toString()
        val entered = CompletableFuture<RequestContext>()
        val cancelled = CompletableFuture<Boolean>()
        val result = CompletableFuture<Int>()
        val replies = AtomicInteger()
        bridge.registerRequest(
            "cancel",
            RequestHandler { _, _, context ->
                context.onCancelled { cancelled.complete(true) }
                entered.complete(context)
            },
        )
        bridge.sendRequest(
            id,
            "cancel",
            byteArrayOf(),
            Reply { status, _ ->
                replies.incrementAndGet()
                result.complete(status)
            },
        )
        val context = entered.get(5, TimeUnit.SECONDS)
        bridge.cancel(id)
        assertTrue(cancelled.get(5, TimeUnit.SECONDS))
        assertEquals(4, result.get(5, TimeUnit.SECONDS))
        context.complete(0, byteArrayOf())
        val drained = CompletableFuture<Boolean>()
        bridge.closeAsync { _, _ -> drained.complete(true) }
        assertTrue(drained.get(5, TimeUnit.SECONDS))
        assertEquals(1, replies.get())
    }

    @Test
    fun bidirectionalReentrantRequest() {
        NativeBridge().use { bridge ->
            val result = CompletableFuture<String>()
            bridge.setReceiver(
                MessageHandler { _, bytes, reply ->
                    reply.complete(0, bytes + "-managed".toByteArray())
                }
            )
            bridge.register(
                "native",
                MessageHandler { _, bytes, reply -> bridge.invokeManaged("managed", bytes, reply) },
            )
            bridge.send(
                "native",
                "hello".toByteArray(),
                Reply { status, bytes ->
                    assertEquals(0, status)
                    result.complete(String(bytes))
                },
            )
            assertEquals("hello-managed", result.get(5, TimeUnit.SECONDS))
        }
    }

    @Test
    fun duplicateReplyIsIgnored() {
        NativeBridge().use { bridge ->
            val finished = CompletableFuture<Boolean>()
            val count = AtomicInteger()
            bridge.register(
                "duplicate",
                MessageHandler { _, _, reply ->
                    reply.complete(0, byteArrayOf())
                    reply.complete(0, byteArrayOf())
                    finished.complete(true)
                },
            )
            bridge.send("duplicate", byteArrayOf(), Reply { _, _ -> count.incrementAndGet() })
            finished.get(5, TimeUnit.SECONDS)
            assertEquals(1, count.get())
        }
    }

    @Test
    fun missingAndDisposedAreExplicit() {
        val bridge = NativeBridge()
        val missing = CompletableFuture<Int>()
        bridge.send("unknown", byteArrayOf(), Reply { status, _ -> missing.complete(status) })
        assertEquals(1, missing.get(5, TimeUnit.SECONDS))
        bridge.close()
        val closed = CompletableFuture<Int>()
        bridge.send("unknown", byteArrayOf(), Reply { status, _ -> closed.complete(status) })
        assertEquals(3, closed.get(5, TimeUnit.SECONDS))
    }

    @Test
    fun closeCompletesPendingAndReleasesLateReply() {
        val bridge = NativeBridge()
        val entered = CompletableFuture<Reply>()
        val result = CompletableFuture<Int>()
        val count = AtomicInteger()
        bridge.register("deferred", MessageHandler { _, _, reply -> entered.complete(reply) })
        bridge.send(
            "deferred",
            byteArrayOf(),
            Reply { status, _ ->
                count.incrementAndGet()
                result.complete(status)
            },
        )
        val late = entered.get(5, TimeUnit.SECONDS)
        bridge.close()
        assertEquals(3, result.get(5, TimeUnit.SECONDS))
        late.complete(0, byteArrayOf())
        assertEquals(1, count.get())
    }
}
