package dev.nativechannels.audio

import android.content.Context
import android.media.AudioAttributes
import android.media.AudioFocusRequest
import android.media.AudioFormat
import android.media.AudioManager
import android.media.AudioTrack
import android.os.Build
import android.os.Handler
import android.os.Looper
import dev.nativechannels.*
import java.util.ArrayDeque
import java.util.concurrent.Executors
import java.util.concurrent.ScheduledExecutorService
import java.util.concurrent.TimeUnit

class AudioStreamerModule : NativeModule {
    override fun register(bridge: NativeBridge) {
        val player = PcmPlayer()
        MethodChannel("plugin.maui.audio.control", bridge).setHandler { call, result ->
            player.control(call, result)
        }
        BasicMessageChannel("plugin.maui.audio.pcm", bridge, BinaryMessageCodec).setHandler {
            bytes,
            result ->
            result.success(byteArrayOf(player.enqueue(bytes as? ByteArray).toByte()))
        }
    }
}

private class PcmPlayer {
    private class Chunk(val bytes: ByteArray) {
        var offset = 0
    }

    private val gate = Any()
    private val chunks = ArrayDeque<Chunk>()
    private var track: AudioTrack? = null
    private var worker: ScheduledExecutorService? = null
    private var lease: MethodResult? = null
    private var identity: ByteArray? = null
    private var state = "stopped"
    private var accepted = 0L
    private var played = 0L
    private var lastHead = 0L
    private var manager: AudioManager? = null
    private var focus: AudioFocusRequest? = null
    private val focusListener =
        AudioManager.OnAudioFocusChangeListener { change ->
            if (change < 0)
                synchronized(gate) {
                    close(ChannelError("audio_interrupted", "Audio focus was lost"))
                }
        }
    private val attributes =
        AudioAttributes.Builder()
            .setUsage(AudioAttributes.USAGE_MEDIA)
            .setContentType(AudioAttributes.CONTENT_TYPE_MUSIC)
            .build()

    fun control(call: MethodCall, result: MethodResult) =
        synchronized(gate) {
            try {
                val token = call.argument("session") as? ByteArray
                if (token == null || token.size != 16)
                    throw ChannelError("invalid_arguments", "Expected a 16-byte audio session ID")
                if (call.method == "open") {
                    open(token, result)
                    return@synchronized
                }
                if (identity != null && !token.contentEquals(identity))
                    throw ChannelError(
                        "audio_session_mismatch",
                        "This audio session is no longer current",
                    )
                when (call.method) {
                    "status" -> {
                        updatePlayed()
                        result.success(
                            mapOf(
                                "state" to state,
                                "bufferedFrames" to (accepted - played).toInt(),
                                "capacityFrames" to 24000,
                                "playedFrames" to played,
                            )
                        )
                    }
                    "pause" -> {
                        requireActive()
                        track!!.pause()
                        state = "paused"
                        result.success(null)
                    }
                    "resume" -> {
                        requireActive()
                        track!!.play()
                        state = "playing"
                        result.success(null)
                    }
                    "stop" -> {
                        close()
                        result.success(null)
                    }
                    else -> result.notImplemented()
                }
            } catch (error: ChannelError) {
                result.error(error.code, error.message)
            } catch (_: Exception) {
                close(ChannelError("audio_device_error", "Native audio operation failed"))
                result.error("audio_device_error", "Native audio operation failed")
            }
        }

    private fun requireActive() {
        if (track == null) throw ChannelError("audio_closed", "No active audio session")
    }

    @Suppress("DEPRECATION")
    private fun open(token: ByteArray, result: MethodResult) {
        if (lease != null) throw ChannelError("audio_busy", "An audio session is already open")
        if (result.isCancelled) return
        identity = token.copyOf()
        lease = result
        result.onCancel { synchronized(gate) { if (identity.contentEquals(token)) close() } }
        if (result.isCancelled) {
            close()
            return
        }
        try {
            manager =
                AudioStreamerInitializer.applicationContext.getSystemService(Context.AUDIO_SERVICE)
                    as AudioManager
            val granted =
                if (Build.VERSION.SDK_INT >= 26) {
                    focus =
                        AudioFocusRequest.Builder(AudioManager.AUDIOFOCUS_GAIN_TRANSIENT)
                            .setAudioAttributes(attributes)
                            .setOnAudioFocusChangeListener(
                                focusListener,
                                Handler(Looper.getMainLooper()),
                            )
                            .build()
                    manager!!.requestAudioFocus(focus!!)
                } else
                    manager!!.requestAudioFocus(
                        focusListener,
                        AudioManager.STREAM_MUSIC,
                        AudioManager.AUDIOFOCUS_GAIN_TRANSIENT,
                    )
            if (granted != AudioManager.AUDIOFOCUS_REQUEST_GRANTED)
                throw ChannelError("audio_focus_denied", "Audio focus was not granted")
            val minimum =
                AudioTrack.getMinBufferSize(
                    48000,
                    AudioFormat.CHANNEL_OUT_MONO,
                    AudioFormat.ENCODING_PCM_16BIT,
                )
            if (minimum <= 0)
                throw ChannelError(
                    "audio_format_unsupported",
                    "PCM16 mono at 48 kHz is unavailable",
                )
            track =
                AudioTrack.Builder()
                    .setAudioAttributes(attributes)
                    .setAudioFormat(
                        AudioFormat.Builder()
                            .setSampleRate(48000)
                            .setChannelMask(AudioFormat.CHANNEL_OUT_MONO)
                            .setEncoding(AudioFormat.ENCODING_PCM_16BIT)
                            .build()
                    )
                    .setBufferSizeInBytes(maxOf(minimum, 9600))
                    .setTransferMode(AudioTrack.MODE_STREAM)
                    .build()
            if (track!!.state != AudioTrack.STATE_INITIALIZED)
                throw ChannelError("audio_device_error", "AudioTrack initialization failed")
            track!!.setStartThresholdInFrames(1)
            accepted = 0
            played = 0
            lastHead = 0
            state = "playing"
            track!!.play()
            worker =
                Executors.newSingleThreadScheduledExecutor { work ->
                    Thread(work, "MauiAudioStreamer").apply { isDaemon = true }
                }
            worker!!.scheduleWithFixedDelay({ pump() }, 0, 5, TimeUnit.MILLISECONDS)
        } catch (error: ChannelError) {
            close(error)
        } catch (_: Exception) {
            close(ChannelError("audio_device_error", "Cannot initialize AudioTrack"))
        }
    }

    fun enqueue(payload: ByteArray?): Int =
        synchronized(gate) {
            if (
                payload == null ||
                    payload.size < 18 ||
                    payload.size > 9616 ||
                    (payload.size - 16) % 2 != 0
            )
                return 4
            val token = identity ?: return 3
            if (!payload.copyOfRange(0, 16).contentEquals(token)) return 3
            if (state == "paused") return 2
            if (state != "playing") return 3
            updatePlayed()
            val frames = (payload.size - 16) / 2
            if (accepted - played + frames > 24000) return 1
            chunks.addLast(Chunk(payload.copyOfRange(16, payload.size)))
            accepted += frames
            0
        }

    private fun updatePlayed() {
        val audio = track ?: return
        val head = audio.playbackHeadPosition.toLong() and 0xffffffffL
        val advance = (head - lastHead) and 0xffffffffL
        played = minOf(accepted, played + advance)
        lastHead = head
    }

    private fun pump() =
        synchronized(gate) {
            if (state != "playing") return@synchronized
            try {
                updatePlayed()
                while (chunks.isNotEmpty()) {
                    val chunk = chunks.first
                    val count =
                        track!!.write(
                            chunk.bytes,
                            chunk.offset,
                            chunk.bytes.size - chunk.offset,
                            AudioTrack.WRITE_NON_BLOCKING,
                        )
                    if (count < 0) throw IllegalStateException("AudioTrack write failed")
                    if (count == 0) break
                    chunk.offset += count
                    if (chunk.offset == chunk.bytes.size) chunks.removeFirst()
                }
            } catch (_: Exception) {
                close(ChannelError("audio_device_error", "AudioTrack playback failed"))
            }
        }

    @Suppress("DEPRECATION")
    private fun close(error: ChannelError? = null) {
        val completion = lease
        lease = null
        identity = null
        state = "stopped"
        worker?.shutdownNow()
        worker = null
        chunks.clear()
        val audio = track
        track = null
        try {
            audio?.pause()
            audio?.flush()
        } catch (_: Exception) {}
        try {
            audio?.release()
        } catch (_: Exception) {}
        accepted = 0
        played = 0
        lastHead = 0
        if (Build.VERSION.SDK_INT >= 26) focus?.let { manager?.abandonAudioFocusRequest(it) }
        else manager?.abandonAudioFocus(focusListener)
        focus = null
        manager = null
        if (completion != null && !completion.isCancelled) {
            if (error != null) completion.error(error.code, error.message)
            else completion.success(null)
        }
    }
}
