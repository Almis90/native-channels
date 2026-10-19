package dev.nativechannels.currency

import java.util.Locale
import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Test

class CurrencySymbolsTests {
    @Test
    fun symbolsUseTheRequestedLocale() {
        assertEquals("$", CurrencySymbols.getSymbol("USD", Locale.US))
        assertEquals("€", CurrencySymbols.getSymbol("EUR", Locale.US))
        assertEquals("£", CurrencySymbols.getSymbol("GBP", Locale.UK))
        assertEquals("US$", CurrencySymbols.getSymbol("USD", Locale.UK))
    }

    @Test
    fun rejectsUnknownAndMalformedCodes() {
        for (code in listOf("ZZZ", "usd", "US", "123", "", "ＵＳＤ")) {
            assertThrows(IllegalArgumentException::class.java) { CurrencySymbols.getSymbol(code) }
        }
    }
}
