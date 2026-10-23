package dev.nativechannels.currency

import dev.nativechannels.MethodChannel
import dev.nativechannels.NativeBridge
import dev.nativechannels.NativeModule

class CurrencyModule : NativeModule {
    override fun register(bridge: NativeBridge) {
        MethodChannel("plugin.maui.currency", bridge).setHandler { call, result ->
            if (call.method != "getSymbol") {
                result.notImplemented()
                return@setHandler
            }
            val code = call.arguments as? String
            if (code == null) {
                result.error("invalid_currency_code", "Expected a three-letter currency code")
                return@setHandler
            }
            try {
                result.success(CurrencySymbols.getSymbol(code))
            } catch (_: IllegalArgumentException) {
                result.error("invalid_currency_code", "Currency code is not recognized", code)
            }
        }
    }
}
