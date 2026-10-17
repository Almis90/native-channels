#pragma once
#include "native_channels.h"
#include <algorithm>
#include <cstring>
#include <map>
#include <stdexcept>
#include <string>
#include <variant>
#include <vector>

namespace nc {
using Bytes = std::vector<uint8_t>;
struct Value {
  using Map = std::map<std::string, Value>;
  using List = std::vector<Value>;
  std::variant<std::monostate, bool, int32_t, int64_t, double, std::string,
               Bytes, Map, List>
      data;
  Value() = default;
  template <class T> Value(T value) : data(std::move(value)) {}
  Value(const char *value) : data(std::string(value)) {}
  template <class T> const T *get() const { return std::get_if<T>(&data); }
};
struct Error : std::runtime_error {
  std::string code;
  Value details;
  Error(std::string code, std::string message, Value details = {})
      : std::runtime_error(message), code(std::move(code)),
        details(std::move(details)) {}
};
// The reference modules use scalar values, byte arrays, lists and string-keyed
// maps. Unsupported codec tags fail explicitly; they are never silently
// coerced.
class Reader {
  const uint8_t *data;
  size_t size, position = 0;
  uint64_t number(size_t width) {
    if (width > size - position)
      throw Error("invalid_arguments", "Truncated channel value");
    uint64_t result = 0;
    for (size_t i = 0; i < width; ++i)
      result |= uint64_t(data[position++]) << (8 * i);
    return result;
  }
  size_t count() {
    auto n = number(1);
    return n < 254 ? n : number(n == 254 ? 2 : 4);
  }
  Value value(int depth) {
    if (depth > 64)
      throw Error("invalid_arguments", "Channel value nesting limit");
    switch (number(1)) {
    case 0:
      return {};
    case 1:
      return true;
    case 2:
      return false;
    case 3:
      return int32_t(number(4));
    case 4:
      return int64_t(number(8));
    case 6: {
      while (position % 8)
        number(1);
      auto bits = number(8);
      double result;
      std::memcpy(&result, &bits, 8);
      return result;
    }
    case 7:
    case 8: {
      auto tag = data[position - 1];
      auto n = count();
      if (n > size - position)
        throw Error("invalid_arguments", "Truncated channel bytes");
      auto start = data + position;
      position += n;
      if (tag == 7)
        return std::string(reinterpret_cast<const char *>(start), n);
      return Bytes(start, start + n);
    }
    case 12: {
      auto n = count();
      if (n > size - position)
        throw Error("invalid_arguments", "Invalid list length");
      Value::List result;
      for (size_t i = 0; i < n; ++i)
        result.push_back(value(depth + 1));
      return result;
    }
    case 13: {
      auto n = count();
      if (n > (size - position) / 2)
        throw Error("invalid_arguments", "Invalid map length");
      Value::Map result;
      for (size_t i = 0; i < n; ++i) {
        auto key = value(depth + 1);
        auto text = key.get<std::string>();
        if (!text)
          throw Error("invalid_arguments", "Expected string map key");
        auto entry = value(depth + 1);
        if (!result.emplace(*text, std::move(entry)).second)
          throw Error("invalid_arguments", "Duplicate map key");
      }
      return result;
    }
    default:
      throw Error("invalid_arguments",
                  "Unsupported reference-module codec tag");
    }
  }

public:
  Reader(const uint8_t *data, size_t size) : data(data), size(size) {}
  Value read() {
    auto result = value(0);
    if (position != size)
      throw Error("invalid_arguments", "Trailing channel bytes");
    return result;
  }
};
class Writer {
  Bytes bytes;
  void number(uint64_t n, size_t width) {
    for (size_t i = 0; i < width; ++i)
      bytes.push_back(uint8_t(n >> (8 * i)));
  }
  void count(size_t n) {
    if (n < 254)
      number(n, 1);
    else if (n <= 65535) {
      number(254, 1);
      number(n, 2);
    } else {
      number(255, 1);
      number(n, 4);
    }
  }
  void value(const Value &v) {
    if (std::holds_alternative<std::monostate>(v.data))
      number(0, 1);
    else if (auto p = v.get<bool>())
      number(*p ? 1 : 2, 1);
    else if (auto p = v.get<int32_t>()) {
      number(3, 1);
      number(uint32_t(*p), 4);
    } else if (auto p = v.get<int64_t>()) {
      number(4, 1);
      number(uint64_t(*p), 8);
    } else if (auto p = v.get<double>()) {
      number(6, 1);
      while (bytes.size() % 8)
        number(0, 1);
      uint64_t bits;
      std::memcpy(&bits, p, 8);
      number(bits, 8);
    } else if (auto p = v.get<std::string>()) {
      number(7, 1);
      count(p->size());
      bytes.insert(bytes.end(), p->begin(), p->end());
    } else if (auto p = v.get<Bytes>()) {
      number(8, 1);
      count(p->size());
      bytes.insert(bytes.end(), p->begin(), p->end());
    } else if (auto p = v.get<Value::Map>()) {
      number(13, 1);
      count(p->size());
      for (auto &[key, item] : *p) {
        value(key);
        value(item);
      }
    } else if (auto p = v.get<Value::List>()) {
      number(12, 1);
      count(p->size());
      for (auto &item : *p)
        value(item);
    }
  }

public:
  Bytes write(const Value &v) {
    value(v);
    return std::move(bytes);
  }
};
class Module {
  nc_reply callback;

public:
  explicit Module(nc_reply callback) : callback(callback) {}
  virtual ~Module() = default;
  void reply(uint64_t id, const Value &value = {}, int status = 0) {
    auto bytes = Writer().write(value);
    callback(id, status, bytes.data(), bytes.size());
  }
  void fail(uint64_t id, const Error &error) {
    reply(id,
          Value::Map{{"code", error.code},
                     {"message", std::string(error.what())},
                     {"details", error.details},
                     {"platform", "linux"}},
          1);
  }
  virtual void request(uint64_t id, const std::string &channel,
                       const std::string &method, const Value &value,
                       const char *locale) = 0;
  virtual void cancel(uint64_t) {}
};
} // namespace nc

// Define nc::make_module in the native adapter. The exported ABI stays language
// neutral.
namespace nc {
Module *make_module(nc_reply);
}
extern "C" {
void *nc_create(nc_reply reply) {
  try {
    return nc::make_module(reply);
  } catch (...) {
    return nullptr;
  }
}
void nc_request(void *module, uint64_t id, const char *channel,
                const char *method, const uint8_t *data, size_t size,
                const char *locale) {
  auto m = static_cast<nc::Module *>(module);
  try {
    m->request(id, channel, method, nc::Reader(data, size).read(), locale);
  } catch (const nc::Error &error) {
    m->fail(id, error);
  } catch (const std::exception &error) {
    m->fail(id, nc::Error("native_error", error.what()));
  } catch (...) {
    m->fail(id, nc::Error("native_error", "Unknown native failure"));
  }
}
void nc_cancel(void *module, uint64_t id) {
  try {
    static_cast<nc::Module *>(module)->cancel(id);
  } catch (...) {
  }
}
void nc_destroy(void *module) { delete static_cast<nc::Module *>(module); }
}
