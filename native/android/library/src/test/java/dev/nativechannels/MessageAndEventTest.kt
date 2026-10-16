package dev.nativechannels

import java.util.UUID
import java.util.concurrent.CompletableFuture
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicInteger
import org.junit.Assert.*
import org.junit.Test

class MessageAndEventTest {
    @Test
    fun codecsPreserveNullEmptyUnicodeAndRejectMalformed() {
        for (codec in listOf(BinaryMessageCodec, StringMessageCodec, JsonMessageCodec)) {
            assertNull(codec.decode(codec.encode(null)))
        }
        assertArrayEquals(byteArrayOf(), BinaryMessageCodec.decode(byteArrayOf(1)))
        assertEquals("", StringMessageCodec.decode(byteArrayOf(1)))
        assertEquals("€😀", StringMessageCodec.decode(StringMessageCodec.encode("€😀")))
        for (data in listOf(byteArrayOf(), byteArrayOf(0, 1), byteArrayOf(2), byteArrayOf(1, -1))) {
            assertThrows(Exception::class.java) { StringMessageCodec.decode(data) }
        }
        assertThrows(Exception::class.java) { StringMessageCodec.encode("\uD800") }
        val source = byteArrayOf(1, 2)
        val encoded = BinaryMessageCodec.encode(source)
        source[0] = 9
        assertArrayEquals(byteArrayOf(1, 2), BinaryMessageCodec.decode(encoded))
    }

    @Test
    fun basicMessagesAreReentrantWithEachCodec() {
        for ((codec, value) in
            listOf(
                JsonMessageCodec to 42,
                StringMessageCodec to "€",
                BinaryMessageCodec to byteArrayOf(1, 2),
            )) {
            NativeBridge().use { bridge ->
                bridge.setRequestReceiver { _, bytes, context -> context.complete(0, bytes) }
                val channel = BasicMessageChannel("basic", bridge, codec)
                channel.setHandler { data, result ->
                    channel.send(data) { reply, error ->
                        assertNull(error)
                        result.success(reply)
                    }
                }
                val reply = CompletableFuture<ByteArray>()
                bridge.sendRequest(
                    UUID.randomUUID().toString(),
                    "basic",
                    codec.encode(value),
                    Reply { status, bytes ->
                        assertEquals(0, status)
                        reply.complete(bytes)
                    },
                )
                assertArrayEquals(codec.encode(value), reply.get(5, TimeUnit.SECONDS))
            }
        }
    }

    @Test
    fun eventCompletionDrainsNullAndCleansUpExactlyOnce() {
        NativeBridge().use { bridge ->
            val values = mutableListOf<Any?>()
            val cleaned = AtomicInteger()
            bridge.setRequestReceiver { _, bytes, context ->
                values.add(JsonMethodCodec.call(bytes).arguments)
                context.complete(0, JsonMethodCodec.success(null))
            }
            EventChannel("events", bridge).setStreamHandler { _, sink ->
                sink.onTermination { cleaned.incrementAndGet() }
                sink.success(null)
                sink.success(42)
                sink.endOfStream()
                sink.endOfStream()
            }
            val done = listen(bridge)
            assertNull(JsonMethodCodec.result(done.second.get(5, TimeUnit.SECONDS)))
            assertEquals(listOf(null, 42), values)
            assertEquals(1, cleaned.get())
        }
    }

    @Test
    fun independentListenersCancellationOverflowAndLateCleanup() {
        NativeBridge().use { bridge ->
            val sinks = java.util.concurrent.LinkedBlockingQueue<EventSink>()
            val cleaned = AtomicInteger()
            bridge.setRequestReceiver { _, _, _
                -> /* Stall the acknowledgement to fill the native queue. */
            }
            EventChannel("events", bridge).setStreamHandler { _, sink -> sinks.put(sink) }
            val first = listen(bridge)
            val a = sinks.poll(5, TimeUnit.SECONDS)!!
            val second = listen(bridge)
            val b = sinks.poll(5, TimeUnit.SECONDS)!!
            bridge.cancel(first.first)
            a.onTermination { cleaned.incrementAndGet() }
            b.onTermination { cleaned.incrementAndGet() }
            assertEquals(1, cleaned.get())
            repeat(66) { b.success(it) }
            val error =
                assertThrows(ChannelError::class.java) {
                    JsonMethodCodec.result(second.second.get(5, TimeUnit.SECONDS))
                }
            assertEquals("event_overflow", error.code)
            assertEquals(2, cleaned.get())
            bridge.close()
            assertEquals(2, cleaned.get())
        }
    }

    private fun listen(bridge: NativeBridge): Pair<String, CompletableFuture<ByteArray>> {
        val id = UUID.randomUUID().toString()
        val done = CompletableFuture<ByteArray>()
        bridge.sendRequest(
            id,
            "events",
            JsonMethodCodec.encodeCall(
                "listen",
                mapOf(
                    "events" to "__maui.events/${UUID.randomUUID().toString().replace("-", "")}",
                    "arguments" to null,
                ),
            ),
            Reply { _, bytes -> done.complete(bytes) },
        )
        return id to done
    }
}
