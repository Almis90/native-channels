package dev.nativechannels

import java.io.File
import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Test

class ProtocolTest {
    @Test
    fun unsupportedAsyncResultCompletesWithSerializationError() {
        var response: ByteArray? = null
        val context =
            object : RequestContext {
                override fun getRequestId() = java.util.UUID.randomUUID().toString()

                override fun isCancelled() = false

                override fun onCancelled(handler: CancellationHandler) {}

                override fun complete(status: Int, payload: ByteArray) {
                    assertEquals(0, status)
                    response = payload
                }
            }
        MethodResult(context).success(java.math.BigDecimal.ONE)
        val error = assertThrows(ChannelError::class.java) { JsonMethodCodec.result(response!!) }
        assertEquals("serialization_error", error.code)
    }

    @Test
    fun throwingApplicationCompletionIsNeverInvokedAgain() {
        val bridge = NativeBridge()
        val entered = java.util.concurrent.CompletableFuture<Boolean>()
        val callbacks = java.util.concurrent.atomic.AtomicInteger()
        bridge.setRequestReceiver { _, _, context ->
            context.complete(0, JsonMethodCodec.success(42))
        }
        MethodChannel("managed", bridge).invoke("echo") { _, _ ->
            callbacks.incrementAndGet()
            entered.complete(true)
            throw IllegalStateException("Application callback failed")
        }
        entered.get(5, java.util.concurrent.TimeUnit.SECONDS)
        val drained = java.util.concurrent.CompletableFuture<Boolean>()
        bridge.closeAsync { _, _ -> drained.complete(true) }
        drained.get(5, java.util.concurrent.TimeUnit.SECONDS)
        assertEquals(1, callbacks.get())
    }

    @Test
    fun sharedFixtures() {
        val fixtures = JSONObject(File("../../../tests/fixtures/values.json").readText())
        val valid = fixtures.getJSONArray("valid")
        for (index in 0 until valid.length()) {
            val value = valid.getJSONArray(index)
            assertEquals(
                value.toString(),
                JsonMethodCodec.encodeValue(JsonMethodCodec.decodeValue(value)).toString(),
            )
        }
        val invalid = fixtures.getJSONArray("invalid")
        for (index in 0 until invalid.length()) {
            assertThrows(Exception::class.java) {
                JsonMethodCodec.decodeValue(invalid.getJSONArray(index))
            }
        }
        assertNull(JsonMethodCodec.result(JsonMethodCodec.success(null)))
        val error =
            assertThrows(ChannelError::class.java) {
                JsonMethodCodec.result(JsonMethodCodec.error(ChannelError("expected", "message")))
            }
        assertEquals("expected", error.code)
    }
}
