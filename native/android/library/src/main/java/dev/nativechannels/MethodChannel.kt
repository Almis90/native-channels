package dev.nativechannels

import android.os.Handler
import android.os.Looper
import java.util.UUID
import org.json.JSONArray
import org.json.JSONObject

class ChannelError(val code: String, override val message: String, val details: Any? = null) :
    Exception(message)

class MethodCall(val method: String, val arguments: Any?) {
    fun argument(name: String): Any? {
        val map =
            arguments as? Map<*, *>
                ?: throw ChannelError("invalid_arguments", "Expected named arguments")
        if (!map.containsKey(name))
            throw ChannelError("invalid_arguments", "Missing argument: $name")
        return map[name]
    }
}

class MethodResult(private val context: RequestContext) {
    val isCancelled: Boolean
        get() = context.isCancelled

    fun onCancel(action: () -> Unit) = context.onCancelled(CancellationHandler(action))

    fun success(value: Any?) {
        val payload =
            try {
                JsonMethodCodec.success(value)
            } catch (_: Exception) {
                error("serialization_error", "Cannot encode native result")
                return
            }
        context.complete(0, payload)
    }

    fun error(code: String, message: String, details: Any? = null) {
        val payload =
            try {
                JsonMethodCodec.error(ChannelError(code, message, details))
            } catch (_: Exception) {
                context.complete(2, byteArrayOf())
                return
            }
        context.complete(0, payload)
    }

    fun notImplemented() =
        context.complete(
            0,
            JSONObject().put("v", 1).put("status", "not_implemented").toString().toByteArray(),
        )
}

/** Native developer API. Only NativeBridge and its callback interfaces are bound to C#. */
class MethodChannel(private val name: String, private val bridge: NativeBridge) {
    fun setHandler(onMainThread: Boolean = false, handler: (MethodCall, MethodResult) -> Unit) {
        bridge.registerRequest(
            name,
            RequestHandler { _, payload, context ->
                val work = Runnable {
                    if (!context.isCancelled) {
                        try {
                            handler(JsonMethodCodec.call(payload), MethodResult(context))
                        } catch (error: ChannelError) {
                            context.complete(0, JsonMethodCodec.error(error))
                        } catch (_: Exception) {
                            context.complete(
                                0,
                                JsonMethodCodec.error(
                                    ChannelError("native_error", "Native handler failed")
                                ),
                            )
                        }
                    }
                }
                if (onMainThread) Handler(Looper.getMainLooper()).post(work) else work.run()
            },
        )
    }

    fun invoke(
        method: String,
        arguments: Any? = null,
        completion: (Any?, ChannelError?) -> Unit,
    ): String {
        val id = UUID.randomUUID().toString()
        val bytes = JsonMethodCodec.encodeCall(method, arguments)
        bridge.invokeManagedRequest(
            id,
            name,
            bytes,
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
                            "Managed request failed",
                        ),
                    )
                else {
                    val value =
                        try {
                            JsonMethodCodec.result(payload)
                        } catch (error: ChannelError) {
                            completion(null, error)
                            return@Reply
                        } catch (_: Exception) {
                            completion(
                                null,
                                ChannelError("serialization_error", "Invalid managed response"),
                            )
                            return@Reply
                        }
                    completion(value, null)
                }
            },
        )
        return id
    }

    fun cancel(requestId: String) = bridge.cancel(requestId)
}

object JsonMethodCodec {
    private const val MAX_BYTES = 16 * 1024 * 1024

    private fun parse(bytes: ByteArray): JSONObject {
        require(bytes.size <= MAX_BYTES)
        val json = JSONObject(String(bytes, Charsets.UTF_8))
        require(json.opt("v") == 1) { "Unsupported protocol version" }
        return json
    }

    fun call(bytes: ByteArray): MethodCall {
        val json = parse(bytes)
        val method =
            json.get("method") as? String
                ?: throw ChannelError("invalid_arguments", "Invalid method name")
        require(
            method.isNotBlank() &&
                method.toByteArray().size <= 256 &&
                method.none { it.isISOControl() }
        )
        return MethodCall(method, decodeValue(json.getJSONArray("arguments")))
    }

    fun encodeCall(method: String, arguments: Any?): ByteArray =
        JSONObject()
            .put("v", 1)
            .put("method", method)
            .put("arguments", encodeValue(arguments))
            .toString()
            .toByteArray()

    fun success(value: Any?): ByteArray =
        JSONObject()
            .put("v", 1)
            .put("status", "success")
            .put("value", encodeValue(value))
            .toString()
            .toByteArray()

    fun error(error: ChannelError): ByteArray =
        JSONObject()
            .put("v", 1)
            .put("status", "error")
            .put("code", error.code)
            .put("message", error.message)
            .put("details", encodeValue(error.details))
            .put("platform", "android")
            .toString()
            .toByteArray()

    fun result(bytes: ByteArray): Any? {
        val json = parse(bytes)
        return when (json.getString("status")) {
            "success" -> decodeValue(json.getJSONArray("value"))
            "not_implemented" -> throw ChannelError("not_implemented", "Method is not implemented")
            "error" ->
                throw ChannelError(
                    json.getString("code"),
                    json.getString("message"),
                    decodeValue(json.getJSONArray("details")),
                )
            else -> throw ChannelError("serialization_error", "Unknown method status")
        }
    }

    fun encodeValue(value: Any?, depth: Int = 0): JSONArray {
        require(depth <= 64) { "Value nesting limit exceeded" }
        val result = JSONArray()
        when (value) {
            null -> result.put("n")
            is Boolean -> result.put("b").put(value)
            is String -> result.put("s").put(value)
            is Int -> result.put("i32").put(value)
            is Long -> result.put("i64").put(value.toString())
            is Double -> {
                require(value.isFinite())
                result.put("f64").put(value)
            }
            is Float -> {
                require(value.isFinite())
                result.put("f64").put(value.toDouble())
            }
            is ByteArray ->
                result
                    .put("bytes")
                    .put(android.util.Base64.encodeToString(value, android.util.Base64.NO_WRAP))
            is List<*> ->
                result
                    .put("list")
                    .put(
                        JSONArray().also { array ->
                            value.forEach { array.put(encodeValue(it, depth + 1)) }
                        }
                    )
            is Map<*, *> ->
                result
                    .put("map")
                    .put(
                        JSONObject().also { map ->
                            value.forEach { (key, item) ->
                                require(key is String)
                                map.put(key, encodeValue(item, depth + 1))
                            }
                        }
                    )
            else -> throw ChannelError("serialization_error", "Unsupported value type")
        }
        return result
    }

    fun decodeValue(value: JSONArray, depth: Int = 0): Any? {
        require(depth <= 64)
        val tag = value.getString(0)
        require(value.length() == if (tag == "n") 1 else 2)
        return when (tag) {
            "n" -> null
            "b" -> value.get(1) as Boolean
            "s" -> value.get(1) as String
            "i32" -> {
                val number = value.get(1)
                require(number is Int || number is Long)
                (number as Number)
                    .toLong()
                    .also { require(it in Int.MIN_VALUE..Int.MAX_VALUE) }
                    .toInt()
            }
            "i64" -> (value.get(1) as String).toLong()
            "f64" -> (value.get(1) as Number).toDouble().also { require(it.isFinite()) }
            "bytes" -> {
                val text = value.get(1) as String
                require(
                    text.matches(
                        Regex("(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?")
                    )
                )
                android.util.Base64.decode(text, android.util.Base64.NO_WRAP)
            }
            "list" ->
                value.getJSONArray(1).let { list ->
                    (0 until list.length()).map { decodeValue(list.getJSONArray(it), depth + 1) }
                }
            "map" ->
                value.getJSONObject(1).let { map ->
                    map.keys().asSequence().associateWith {
                        decodeValue(map.getJSONArray(it), depth + 1)
                    }
                }
            else -> throw ChannelError("serialization_error", "Unknown value tag")
        }
    }
}
