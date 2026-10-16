import Foundation

let us = NSLocale(localeIdentifier: "en_US")
let uk = NSLocale(localeIdentifier: "en_GB")
let usd = try CurrencySymbols.getSymbol("USD", locale: us)
let eur = try CurrencySymbols.getSymbol("EUR", locale: us)
let gbp = try CurrencySymbols.getSymbol("GBP", locale: uk)
let foreignUsd = try CurrencySymbols.getSymbol("USD", locale: uk)
precondition(usd == "$" && eur == "€" && gbp == "£" && foreignUsd == "US$")
for code in ["ZZZ", "usd", "US", "123", "", "ＵＳＤ"] {
    do {
        _ = try CurrencySymbols.getSymbol(code, locale: us)
        fatalError("Accepted invalid currency code: \(code)")
    } catch is InvalidCurrencyCode {
        // Expected: callers receive a structured invalid_currency_code error.
    }
}
print("PASS CurrencySymbols: localized symbols and invalid codes")
