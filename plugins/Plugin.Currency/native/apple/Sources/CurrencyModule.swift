import Foundation
import NativeChannelsNative

@objc(MCCurrencyModule)
public final class CurrencyModule: NSObject, NativeModule {
    public required override init() {
        super.init()
    }

    public func register(with bridge: NativeBridge) {
        precondition(
            MethodChannel("plugin.maui.currency", bridge: bridge).setHandler(onMainThread: false) { call, result in
                guard call.method == "getSymbol" else {
                    result.notImplemented()
                    return
                }
                guard let code = call.arguments as? String else {
                    result.failure(
                        "invalid_currency_code", message: "Expected a three-letter currency code", details: nil)
                    return
                }
                do {
                    result.success(try CurrencySymbols.getSymbol(code))
                } catch {
                    result.failure("invalid_currency_code", message: "Currency code is not recognized", details: code)
                }
            })
    }
}
