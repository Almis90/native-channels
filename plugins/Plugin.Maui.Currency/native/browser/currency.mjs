// Uses the browser's CLDR-backed Intl data; no managed or embedded currency table.
export function register(context) {
    if (typeof Intl.supportedValuesOf !== 'function')
        throw new Error('Currency requires Intl.supportedValuesOf in this browser.');
    const currencies = new Set(Intl.supportedValuesOf('currency'));
    return context.registerMethodHandler('plugin.maui.currency', (method, code) => {
        if (method !== 'getSymbol') return context.notImplemented;
        if (typeof code !== 'string' || !/^[A-Z]{3}$/u.test(code) || !currencies.has(code))
            throw new context.ChannelError('invalid_currency_code', 'Unknown ISO currency code.', code);
        const formatter = new Intl.NumberFormat(globalThis.navigator?.languages, {
            style: 'currency', currency: code, currencyDisplay: 'symbol'
        });
        const symbol = formatter.formatToParts(0).find(part => part.type === 'currency')?.value;
        if (!symbol) throw new context.ChannelError('currency_lookup_failed', 'Intl returned no currency symbol.', code);
        return symbol;
    });
}
