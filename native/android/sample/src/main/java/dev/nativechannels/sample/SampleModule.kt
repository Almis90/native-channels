package dev.nativechannels.sample

import android.os.Build
import android.os.Debug
import android.os.Looper
import dev.nativechannels.*

class SampleModule : NativeModule {
    override fun register(bridge: NativeBridge) {
        val activeStreams = java.util.concurrent.atomic.AtomicInteger()
        val streamIds = java.util.concurrent.atomic.AtomicInteger()
        for ((name, codec) in
            listOf(
                "json" to JsonMessageCodec,
                "string" to StringMessageCodec,
                "binary" to BinaryMessageCodec,
                "standard" to StandardMessageCodec,
            )) {
            val messages = BasicMessageChannel("sample.messages.$name", bridge, codec)
            messages.setHandler { value, result ->
                messages.send(value) { reply, error ->
                    if (error != null) result.failure() else result.success(reply)
                }
            }
        }
        EventChannel("sample.events", bridge).setStreamHandler { mode, sink ->
            activeStreams.incrementAndGet()
            sink.onTermination { activeStreams.decrementAndGet() }
            when (mode) {
                "live" -> sink.success(streamIds.incrementAndGet())
                "error" -> {
                    sink.success(1)
                    sink.error("sample_stream_error", "Native stream failed", 42)
                }
                else -> {
                    sink.success(null)
                    sink.success(42)
                    sink.endOfStream()
                }
            }
        }
        val channel = MethodChannel("sample", bridge)
        channel.setHandler { call, result ->
            when (call.method) {
                "activeStreams" -> result.success(activeStreams.get())
                "memory" -> {
                    val runtime = Runtime.getRuntime()
                    result.success(
                        mapOf(
                            "processPss" to Debug.getPss() * 1024L,
                            "nativeHeapAllocated" to Debug.getNativeHeapAllocatedSize(),
                            "jvmUsed" to runtime.totalMemory() - runtime.freeMemory(),
                        )
                    )
                }
                "add" -> result.success((call.argument("a") as Int) + (call.argument("b") as Int))
                "device" ->
                    result.success(
                        mapOf(
                            "platform" to "android",
                            "name" to Build.MODEL,
                            "id" to Long.MAX_VALUE,
                        )
                    )
                "echo" -> result.success(call.arguments)
                "null" -> result.success(null)
                "fail" ->
                    result.error(
                        "sample_error",
                        "Sample native failure",
                        mapOf("retryable" to false),
                    )
                "callback" ->
                    channel.invoke("getToken") { value, error ->
                        if (error != null) result.error(error.code, error.message)
                        else result.success("native:$value")
                    }
                "delay" -> {
                    val milliseconds = (call.arguments as Int).toLong()
                    val worker = Thread {
                        try {
                            Thread.sleep(milliseconds)
                            if (!result.isCancelled) result.success(milliseconds.toInt())
                        } catch (_: InterruptedException) {
                            /* Cancellation already completes the bridge reply. */
                        }
                    }
                    worker.isDaemon = true
                    result.onCancel { worker.interrupt() }
                    worker.start()
                }
                else -> result.notImplemented()
            }
        }
        MethodChannel("sample.ui", bridge).setHandler(onMainThread = true) { _, result ->
            result.success(Looper.myLooper() == Looper.getMainLooper())
        }
    }
}
