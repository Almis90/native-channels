package dev.nativechannels

import java.io.File
import org.json.JSONArray
import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Test

class StandardMessageCodecTest {
    private val codec = StandardMessageCodec

    private fun hex(text: String) = text.chunked(2).map { it.toInt(16).toByte() }.toByteArray()

    private fun value(value: JSONArray): Any? =
        when (value.getString(0)) {
            "n" -> null
            "b" -> value.getBoolean(1)
            "s" -> value.getString(1)
            "i32" -> value.getInt(1)
            "i64" -> value.getString(1).toLong()
            "f64" -> value.getDouble(1)
            "bytes" ->
                value.getJSONArray(1).let { a -> ByteArray(a.length()) { a.getInt(it).toByte() } }
            "i32s" -> value.getJSONArray(1).let { a -> IntArray(a.length()) { a.getInt(it) } }
            "i64s" ->
                value.getJSONArray(1).let { a ->
                    LongArray(a.length()) { a.getString(it).toLong() }
                }
            "f32s" ->
                value.getJSONArray(1).let { a ->
                    FloatArray(a.length()) { a.getDouble(it).toFloat() }
                }
            "f64s" -> value.getJSONArray(1).let { a -> DoubleArray(a.length()) { a.getDouble(it) } }
            "list" ->
                value.getJSONArray(1).let { a -> List(a.length()) { value(a.getJSONArray(it)) } }
            "map" ->
                value.getJSONArray(1).let { a ->
                    StandardMessageMap(
                        List(a.length()) {
                            a.getJSONArray(it).let { pair ->
                                value(pair.getJSONArray(0)) to value(pair.getJSONArray(1))
                            }
                        }
                    )
                }
            else -> error("Invalid fixture")
        }

    @Test
    fun sharedExactBytesAndMalformedFixtures() {
        val fixture = JSONObject(File("../../../tests/fixtures/standard-values.json").readText())
        val valid = fixture.getJSONArray("valid")
        for (index in 0 until valid.length()) {
            val item = valid.getJSONObject(index)
            val bytes = hex(item.getString("hex"))
            assertArrayEquals(
                item.getString("name"),
                bytes,
                codec.encode(value(item.getJSONArray("value"))),
            )
            assertArrayEquals(bytes, codec.encode(codec.decode(bytes)))
            for (length in bytes.indices) assertThrows(ChannelError::class.java) {
                codec.decode(bytes.copyOf(length))
            }
        }
        val invalid = fixture.getJSONArray("invalid")
        for (index in 0 until invalid.length()) assertThrows(ChannelError::class.java) {
            codec.decode(hex(invalid.getString(index)))
        }
    }

    @Test
    fun boundariesLimitsAndIeeeValues() {
        for (count in listOf(253, 254, 65535, 65536)) {
            val bytes = ByteArray(count)
            val wire = codec.encode(bytes)
            assertEquals(
                if (count < 254) count else if (count <= 65535) 254 else 255,
                wire[1].toInt() and 255,
            )
            assertArrayEquals(bytes, codec.decode(wire) as ByteArray)
            val ints = IntArray(count) { it }
            assertArrayEquals(ints, codec.decode(codec.encode(ints)) as IntArray)
        }
        val floats =
            floatArrayOf(Float.NaN, Float.POSITIVE_INFINITY, Float.NEGATIVE_INFINITY, -0.0f)
        assertArrayEquals(
            floats.map { it.toRawBits() }.toIntArray(),
            (codec.decode(codec.encode(floats)) as FloatArray).map { it.toRawBits() }.toIntArray(),
        )
        for (number in
            listOf(
                Double.NaN,
                Double.POSITIVE_INFINITY,
                Double.NEGATIVE_INFINITY,
                -0.0,
            )) assertEquals(
            number.toRawBits(),
            (codec.decode(codec.encode(number)) as Double).toRawBits(),
        )
        val cycle = mutableListOf<Any?>()
        cycle.add(cycle)
        assertThrows(ChannelError::class.java) { codec.encode(cycle) }
        val deep = ByteArray(132) { if (it % 2 == 0) 12 else 1 } + byteArrayOf(0)
        assertThrows(ChannelError::class.java) { codec.decode(deep) }
        assertThrows(ChannelError::class.java) { codec.decode(ByteArray(16 * 1024 * 1024 + 1)) }
        assertThrows(ChannelError::class.java) { codec.encode(ByteArray(16 * 1024 * 1024)) }
        assertThrows(ChannelError::class.java) { codec.encode("\uD800") }
        assertThrows(ChannelError::class.java) { codec.encode(java.math.BigInteger.ONE) }
        val input = byteArrayOf(1, 2)
        val encoded = codec.encode(input)
        input[0] = 9
        val decoded = codec.decode(encoded) as ByteArray
        encoded[2] = 8
        assertArrayEquals(byteArrayOf(1, 2), decoded)
    }
}
