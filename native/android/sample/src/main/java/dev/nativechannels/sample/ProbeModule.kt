package dev.nativechannels.sample

import dev.nativechannels.*

class ProbeModule : NativeModule {
    override fun register(bridge: NativeBridge) {
        bridge.register(
            "probe",
            MessageHandler { _, payload, reply ->
                bridge.invokeManaged(
                    "managed",
                    payload,
                    Reply { status, result ->
                        if (status != 0) reply.complete(status, result)
                        else reply.complete(0, "Kotlin:".toByteArray() + result)
                    },
                )
            },
        )
    }
}
