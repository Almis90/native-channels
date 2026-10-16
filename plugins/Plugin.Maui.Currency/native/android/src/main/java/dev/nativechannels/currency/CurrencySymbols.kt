package dev.nativechannels.currency

import java.util.Currency
import java.util.Locale

internal object CurrencySymbols {
    private val availableCodes = Currency.getAvailableCurrencies().map { it.currencyCode }.toSet()

    fun getSymbol(
        code: String,
        locale: Locale = Locale.getDefault(Locale.Category.DISPLAY),
    ): String {
        require(code.length == 3 && code.all { it in 'A'..'Z' }) {
            "Expected an uppercase currency code"
        }
        require(code in availableCodes) { "Currency code is not recognized" }
        return Currency.getInstance(code).getSymbol(locale)
    }
}
