package dev.nativechannels

import android.content.Context
import android.os.Looper
import android.view.View

/** Generic binding surface. Each host uses a dedicated bridge, closed by the managed owner. */
class NativeViewHost(
    className: String,
    context: Context,
    bridge: NativeBridge,
    instanceId: String,
    channelName: String,
    arguments: ByteArray,
) : AutoCloseable {
    private var instance: NativeViewInstance? = null

    init {
        checkMainThread()
        require(arguments.size <= 16 * 1024 * 1024) { "Creation arguments too large" }
        val factory =
            Class.forName(className).getDeclaredConstructor().newInstance() as NativeViewFactory
        val created = factory.create(context, bridge, instanceId, channelName, arguments.copyOf())
        try {
            require(created.view.parent == null) {
                "A view factory must return a new, unattached view"
            }
            instance = created
        } catch (error: Exception) {
            created.close()
            throw error
        }
    }

    fun getView(): View {
        checkMainThread()
        return checkNotNull(instance) { "Native view disposed" }.view
    }

    override fun close() {
        checkMainThread()
        val old = instance ?: return
        instance = null
        old.close()
    }

    private fun checkMainThread() {
        check(Looper.myLooper() == Looper.getMainLooper()) { "Native views require the UI thread" }
    }
}
