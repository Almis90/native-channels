#include "channel.hpp"
#include <unicode/ucurr.h>
#include <unicode/ustring.h>

namespace nc {
class Currency final : public Module {
public:
  using Module::Module;
  void request(uint64_t id, const std::string &channel,
               const std::string &method, const Value &value,
               const char *locale) override {
    if (channel != "plugin.maui.currency" || method != "getSymbol") {
      reply(id, {}, 2);
      return;
    }
    auto code = value.get<std::string>();
    if (!code || code->size() != 3 ||
        !std::all_of(code->begin(), code->end(),
                     [](char c) { return c >= 'A' && c <= 'Z'; }))
      throw Error("invalid_currency_code",
                  "Expected an uppercase three-letter currency code");
    UChar currency[] = {UChar((*code)[0]), UChar((*code)[1]), UChar((*code)[2]),
                        0};
    if (ucurr_getNumericCode(currency) == 0)
      throw Error("invalid_currency_code", "Currency code is not recognized",
                  *code);
    UErrorCode error = U_ZERO_ERROR;
    int32_t length = 0;
    auto name = ucurr_getName(currency, locale, UCURR_SYMBOL_NAME, nullptr,
                              &length, &error);
    if (U_FAILURE(error) || !name || length <= 0)
      throw Error("currency_lookup_failed", u_errorName(error));
    error = U_ZERO_ERROR;
    int32_t size = 0;
    u_strToUTF8(nullptr, 0, &size, name, length, &error);
    if (error != U_BUFFER_OVERFLOW_ERROR && U_FAILURE(error))
      throw Error("currency_lookup_failed", u_errorName(error));
    std::string symbol(size, '\0');
    error = U_ZERO_ERROR;
    u_strToUTF8(symbol.data(), size, nullptr, name, length, &error);
    if (U_FAILURE(error))
      throw Error("currency_lookup_failed", u_errorName(error));
    reply(id, symbol);
  }
};
Module *make_module(nc_reply reply) { return new Currency(reply); }
} // namespace nc
