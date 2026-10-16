package dev.nativechannels

import java.io.ByteArrayOutputStream
import java.nio.ByteBuffer
import java.nio.CharBuffer
import java.nio.charset.CodingErrorAction

/** Retains arbitrary keys, including null and collections, without host-language key coercion. */
data class StandardMessageMap(val entries: List<Pair<Any?, Any?>>)

/** Little-endian tagged values. JSON remains the default channel codec. */
object StandardMessageCodec : MessageCodec {
    private const val MAX_BYTES = 16 * 1024 * 1024
    private const val MAX_DEPTH = 64

    private fun invalid(): Nothing =
        throw ChannelError("serialization_error", "Malformed or unsupported standard message value")

    private fun <T> checked(action: () -> T): T =
        try {
            action()
        } catch (error: ChannelError) {
            throw error
        } catch (_: Exception) {
            invalid()
        }

    override fun encode(value: Any?): ByteArray = checked {
        Writer().apply { value(value, 0) }.bytes()
    }

    override fun decode(bytes: ByteArray): Any? = checked {
        if (bytes.size > MAX_BYTES) invalid()
        val reader = Reader(bytes)
        val value = reader.value(0)
        if (reader.remaining != 0) invalid()
        value
    }

    private class Writer {
        private val output = ByteArrayOutputStream()

        fun bytes(): ByteArray = output.toByteArray()

        private fun ensure(count: Int) {
            if (count < 0 || count > MAX_BYTES - output.size()) invalid()
        }

        private fun byte(value: Int) {
            ensure(1)
            output.write(value)
        }

        private fun raw(bytes: ByteArray) {
            ensure(bytes.size)
            output.write(bytes)
        }

        private fun number(value: Long, width: Int) {
            ensure(width)
            for (i in 0 until width) output.write((value ushr (8 * i)).toInt())
        }

        private fun size(count: Int) {
            if (count < 0) invalid()
            when {
                count < 254 -> byte(count)
                count <= 65535 -> {
                    byte(254)
                    number(count.toLong(), 2)
                }
                else -> {
                    byte(255)
                    number(count.toLong(), 4)
                }
            }
        }

        private fun align(width: Int) {
            while (output.size() % width != 0) byte(0)
        }

        private fun array(tag: Int, count: Int, width: Int, bits: (Int) -> Long) {
            byte(tag)
            size(count)
            align(width)
            if (count > (MAX_BYTES - output.size()) / width) invalid()
            for (index in 0 until count) number(bits(index), width)
        }

        fun value(value: Any?, depth: Int) {
            if (depth > MAX_DEPTH) invalid()
            when (value) {
                null -> byte(0)
                is Boolean -> byte(if (value) 1 else 2)
                is Byte,
                is Short,
                is Int -> {
                    byte(3)
                    number((value as Number).toLong(), 4)
                }
                is Long -> {
                    byte(4)
                    number(value, 8)
                }
                is Float -> value(value.toDouble(), depth)
                is Double -> {
                    byte(6)
                    align(8)
                    number(value.toRawBits(), 8)
                }
                is String -> {
                    // UTF-8 needs at least one byte per UTF-16 code unit; reject huge strings
                    // before allocation.
                    ensure(value.length)
                    val buffer =
                        Charsets.UTF_8.newEncoder()
                            .onMalformedInput(CodingErrorAction.REPORT)
                            .onUnmappableCharacter(CodingErrorAction.REPORT)
                            .encode(CharBuffer.wrap(value))
                    val bytes = ByteArray(buffer.remaining()).also { buffer.get(it) }
                    byte(7)
                    size(bytes.size)
                    raw(bytes)
                }
                is ByteArray -> {
                    byte(8)
                    size(value.size)
                    raw(value)
                }
                is IntArray -> array(9, value.size, 4) { value[it].toLong() }
                is LongArray -> array(10, value.size, 8) { value[it] }
                is DoubleArray -> array(11, value.size, 8) { value[it].toRawBits() }
                is FloatArray -> array(14, value.size, 4) { value[it].toRawBits().toLong() }
                is StandardMessageMap -> {
                    byte(13)
                    size(value.entries.size)
                    for ((key, item) in value.entries) {
                        value(key, depth + 1)
                        value(item, depth + 1)
                    }
                }
                is Map<*, *> -> {
                    byte(13)
                    size(value.size)
                    for ((key, item) in value) {
                        value(key, depth + 1)
                        value(item, depth + 1)
                    }
                }
                is List<*> -> {
                    byte(12)
                    size(value.size)
                    for (item in value) value(item, depth + 1)
                }
                else -> invalid()
            }
        }
    }

    private class Reader(private val bytes: ByteArray) {
        private var position = 0
        val remaining: Int
            get() = bytes.size - position

        private fun need(count: Int) {
            if (count < 0 || count > remaining) invalid()
        }

        private fun byte(): Int {
            need(1)
            return bytes[position++].toInt() and 255
        }

        private fun raw(count: Int): ByteArray {
            need(count)
            return bytes.copyOfRange(position, position + count).also { position += count }
        }

        private fun number(width: Int): Long {
            need(width)
            var value = 0L
            for (index in 0 until width) value = value or (byte().toLong() shl (index * 8))
            return value
        }

        private fun size(): Int {
            val first = byte()
            val count =
                when (first) {
                    254 -> number(2)
                    255 -> number(4)
                    else -> first.toLong()
                }
            if (count > Int.MAX_VALUE) invalid()
            return count.toInt()
        }

        private fun align(width: Int) {
            while (position % width != 0) if (byte() != 0) invalid()
        }

        private fun arrayCount(width: Int): Int {
            val count = size()
            align(width)
            if (count > remaining / width) invalid()
            return count
        }

        fun value(depth: Int): Any? {
            if (depth > MAX_DEPTH) invalid()
            return when (byte()) {
                0 -> null
                1 -> true
                2 -> false
                3 -> number(4).toInt()
                4 -> number(8)
                6 -> {
                    align(8)
                    Double.fromBits(number(8))
                }
                7 ->
                    Charsets.UTF_8.newDecoder()
                        .onMalformedInput(CodingErrorAction.REPORT)
                        .onUnmappableCharacter(CodingErrorAction.REPORT)
                        .decode(ByteBuffer.wrap(raw(size())))
                        .toString()
                8 -> raw(size())
                9 -> IntArray(arrayCount(4)) { number(4).toInt() }
                10 -> LongArray(arrayCount(8)) { number(8) }
                11 -> DoubleArray(arrayCount(8)) { Double.fromBits(number(8)) }
                14 -> FloatArray(arrayCount(4)) { Float.fromBits(number(4).toInt()) }
                12 -> {
                    val count = size()
                    if (count > remaining) invalid()
                    List(count) { value(depth + 1) }
                }
                13 -> {
                    val count = size()
                    if (count > remaining / 2) invalid()
                    val entries = List(count) { value(depth + 1) to value(depth + 1) }
                    val named = linkedMapOf<String, Any?>()
                    var allNamed = true
                    for ((key, item) in entries) {
                        if (key !is String || named.containsKey(key)) allNamed = false
                        else named[key] = item
                    }
                    if (allNamed) named else StandardMessageMap(entries)
                }
                else -> invalid()
            }
        }
    }
}
