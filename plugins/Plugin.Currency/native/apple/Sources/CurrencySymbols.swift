import Foundation

struct InvalidCurrencyCode: Error {}

enum CurrencySymbols {
    static func getSymbol(_ code: String, locale: NSLocale = NSLocale.current as NSLocale) throws -> String {
        guard code.utf8.count == 3,
            code.utf8.allSatisfy({ $0 >= 65 && $0 <= 90 }),
            NSLocale.isoCurrencyCodes.contains(code)
        else {
            throw InvalidCurrencyCode()
        }
        return locale.displayName(forKey: .currencySymbol, value: code) ?? code
    }
}
