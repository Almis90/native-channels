package dev.nativechannels.sample

import android.content.Context
import android.os.Looper
import android.widget.Button
import dev.nativechannels.*

class ButtonViewFactory : NativeViewFactory {
    override fun create(
        context: Context,
        bridge: NativeBridge,
        instanceId: String,
        channelName: String,
        arguments: ByteArray,
    ): NativeViewInstance {
        val options = JsonMessageCodec.decode(arguments) as? Map<*, *>
        return ButtonInstance(
            context,
            bridge,
            instanceId,
            channelName,
            options?.get("title") as? String ?: "Native button",
        )
    }
}

private class ButtonInstance(
    context: Context,
    bridge: NativeBridge,
    private val instanceId: String,
    channelName: String,
    private var title: String,
) : NativeViewInstance {
    private val button = Button(context)
    private val channel = MethodChannel(channelName, bridge)
    private var count = 0
    private var closed = false

    init {
        update()
        button.setOnClickListener { tap(null) }
        channel.setHandler(onMainThread = true) { call, result ->
            check(Looper.myLooper() == Looper.getMainLooper())
            if (closed) result.error("view_disposed", "The native view is detached")
            else
                when (call.method) {
                    "getState" ->
                        result.success(
                            mapOf(
                                "instanceId" to instanceId,
                                "count" to count,
                                "title" to title,
                                "uiThread" to true,
                            )
                        )
                    "setTitle" -> {
                        title =
                            call.arguments as? String
                                ?: throw ChannelError("invalid_arguments", "Expected title")
                        update()
                        result.success(null)
                    }
                    "simulateTap" -> tap(result)
                    "wait" -> {
                        /* Pending SDK-style work is cancelled when this view's bridge closes. */
                    }
                    else -> result.notImplemented()
                }
        }
    }

    private fun update() {
        button.text = "$title · $count taps"
    }

    private fun tap(result: MethodResult?) {
        if (closed) return
        count++
        update()
        channel.invoke("clicked", mapOf("instanceId" to instanceId, "count" to count)) {
            value,
            error ->
            if (error != null) result?.error(error.code, error.message) else result?.success(value)
        }
    }

    override fun getView(): android.view.View = button

    override fun close() {
        check(Looper.myLooper() == Looper.getMainLooper())
        closed = true
        button.setOnClickListener(null)
    }
}
