package dev.nativechannels

import java.nio.ByteBuffer
import java.nio.charset.CodingErrorAction
import java.util.UUID
import org.json.JSONArray

interface MessageCodec {
    fun encode(value: Any?): ByteArray

    fun decode(bytes: ByteArray): Any?
}

object JsonMessageCodec : MessageCodec {
    override fun encode(value: Any?): ByteArray =
        JsonMethodCodec.encodeValue(value).toString().toByteArray()

    override fun decode(bytes: ByteArray): Any? {
        require(bytes.size <= 16 * 1024 * 1024)
        return JsonMethodCodec.decodeValue(JSONArray(decodeUtf8(bytes)))
    }
}

object BinaryMessageCodec : MessageCodec {
    override fun encode(value: Any?): ByteArray =
        when (value) {
            null -> byteArrayOf(0)
            is ByteArray -> byteArrayOf(1) + value
            else -> throw ChannelError("serialization_error", "Expected bytes or null")
        }

    override fun decode(bytes: ByteArray): ByteArray? {
        require(bytes.size <= 16 * 1024 * 1024 && bytes.isNotEmpty())
        if (bytes[0] == 0.toByte()) {
            require(bytes.size == 1)
            return null
        }
        require(bytes[0] == 1.toByte())
        return bytes.copyOfRange(1, bytes.size)
    }
}

object StringMessageCodec : MessageCodec {
    override fun encode(value: Any?): ByteArray {
        if (value == null) return byteArrayOf(0)
        require(value is String)
        val encoded =
            Charsets.UTF_8.newEncoder()
                .onMalformedInput(CodingErrorAction.REPORT)
                .encode(java.nio.CharBuffer.wrap(value))
        return byteArrayOf(1) + ByteArray(encoded.remaining()).also { encoded.get(it) }
    }

    override fun decode(bytes: ByteArray): String? =
        BinaryMessageCodec.decode(bytes)?.let { decodeUtf8(it) }
}

private fun decodeUtf8(bytes: ByteArray): String =
    Charsets.UTF_8.newDecoder()
        .onMalformedInput(CodingErrorAction.REPORT)
        .onUnmappableCharacter(CodingErrorAction.REPORT)
        .decode(ByteBuffer.wrap(bytes))
        .toString()

class MessageResult(private val context: RequestContext, private val codec: MessageCodec) {
    fun onCancel(action: () -> Unit) = context.onCancelled(CancellationHandler(action))

    fun failure() = context.complete(2, byteArrayOf())

    fun success(value: Any?) {
        try {
            context.complete(0, codec.encode(value))
        } catch (_: Exception) {
            context.complete(2, byteArrayOf())
        }
    }
}

/** Both peers must select the same codec. A reply contains a value, not a method envelope. */
class BasicMessageChannel(
    private val name: String,
    private val bridge: NativeBridge,
    private val codec: MessageCodec = JsonMessageCodec,
) {
    fun setHandler(handler: (Any?, MessageResult) -> Unit) {
        bridge.registerRequest(
            name,
            RequestHandler { _, payload, context ->
                if (!context.isCancelled) {
                    try {
                        handler(codec.decode(payload), MessageResult(context, codec))
                    } catch (_: Exception) {
                        context.complete(2, byteArrayOf())
                    }
                }
            },
        )
    }

    fun send(value: Any?, completion: (Any?, ChannelError?) -> Unit): String {
        val id = UUID.randomUUID().toString()
        bridge.invokeManagedRequest(
            id,
            name,
            codec.encode(value),
            Reply { status, payload ->
                if (status != 0)
                    completion(
                        null,
                        ChannelError(
                            when (status) {
                                1 -> "channel_not_found"
                                3 -> "disposed"
                                4 -> "cancelled"
                                else -> "transport_error"
                            },
                            "Managed message failed",
                        ),
                    )
                else {
                    val decoded =
                        try {
                            codec.decode(payload)
                        } catch (_: Exception) {
                            completion(
                                null,
                                ChannelError("serialization_error", "Invalid managed message"),
                            )
                            return@Reply
                        }
                    completion(decoded, null)
                }
            },
        )
        return id
    }

    fun cancel(requestId: String) = bridge.cancel(requestId)
}
