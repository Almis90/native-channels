package dev.nativechannels.volume

import android.content.Context
import android.database.ContentObserver
import android.media.AudioManager
import android.net.Uri
import android.os.Handler
import android.os.Looper
import android.provider.Settings
import dev.nativechannels.EventChannel
import dev.nativechannels.EventSink
import dev.nativechannels.NativeBridge
import dev.nativechannels.NativeModule

class VolumeMonitorModule : NativeModule {
    override fun register(bridge: NativeBridge) {
        EventChannel("plugin.maui.volume", bridge).setStreamHandler { _, sink ->
            val subscription = VolumeObservation(sink)
            sink.onTermination { subscription.stop() }
            subscription.start()
        }
    }
}

/** All registration, callbacks, and cleanup run on the main looper. */
private class VolumeObservation(private val sink: EventSink) {
    private val handler = Handler(Looper.getMainLooper())
    private val context = VolumeMonitorInitializer.applicationContext
    private val resolver = context.contentResolver
    private val audio = context.getSystemService(Context.AUDIO_SERVICE) as AudioManager
    private var closed = false
    private var registered = false
    private var lastLevel: Double? = null
    private val observer =
        object : ContentObserver(handler) {
            override fun onChange(selfChange: Boolean) = emit()

            override fun onChange(selfChange: Boolean, uri: Uri?) = emit()
        }

    fun start() {
        handler.post {
            if (!closed) {
                try {
                    resolver.registerContentObserver(Settings.System.CONTENT_URI, true, observer)
                    registered = true
                    emit()
                } catch (_: Exception) {
                    fail()
                }
            }
        }
    }

    private fun emit() {
        if (closed) return
        try {
            val maximum = audio.getStreamMaxVolume(AudioManager.STREAM_MUSIC)
            val current = audio.getStreamVolume(AudioManager.STREAM_MUSIC)
            check(maximum > 0 && current in 0..maximum)
            val level = current.toDouble() / maximum
            if (level != lastLevel) {
                lastLevel = level
                sink.success(level)
            }
        } catch (_: Exception) {
            fail()
        }
    }

    private fun fail() {
        close()
        sink.error("volume_unavailable", "Cannot observe the media volume")
    }

    fun stop() {
        handler.post { close() }
    }

    private fun close() {
        if (closed) return
        closed = true
        if (registered) {
            registered = false
            resolver.unregisterContentObserver(observer)
        }
    }
}
