#pragma once
#include "channel.hpp"
#include <cmath>
#include <exception>

// All calls, including toolkit polling and destruction, run on the host UI
// thread.
class PdfModule : public nc::Module {
protected:
  std::string identity;
  int page = 0, count = 0;
  double zoom = 1;
  std::exception_ptr failure;
  virtual void open(const std::string &) = 0;
  virtual void update() = 0;

public:
  using Module::Module;
  virtual uintptr_t create(uintptr_t, const char *) = 0;
  virtual void poll() = 0;
  void pollSafely() noexcept {
    try {
      if (!failure)
        poll();
    } catch (...) {
      failure = std::current_exception();
    }
  }
  void request(uint64_t id, const std::string &, const std::string &method,
               const nc::Value &value, const char *) override {
    if (failure)
      std::rethrow_exception(failure);
    if (method == "open") {
      auto path = value.get<std::string>();
      if (!path || path->empty() || path->front() != '/' ||
          path->find('\0') != std::string::npos)
        throw nc::Error("invalid_arguments", "Use an absolute local PDF path");
      open(*path);
      page = 1;
      zoom = 1;
      update();
    } else if (method == "goToPage") {
      auto number = value.get<int32_t>();
      if (!number || *number < 1 || *number > count)
        throw nc::Error("invalid_page", "Page is outside the document");
      page = *number;
      update();
    } else if (method == "setZoom") {
      auto number = value.get<double>();
      if (!number || !std::isfinite(*number) || *number < 1 || *number > 4)
        throw nc::Error("invalid_zoom", "Zoom must be between 1 and 4");
      if (!count)
        throw nc::Error("document_not_open", "Open a PDF first");
      zoom = *number;
      update();
    } else if (method != "getState") {
      reply(id, {}, 2);
      return;
    }
    reply(id, nc::Value::Map{{"instanceId", identity},
                             {"page", int32_t(page)},
                             {"pageCount", int32_t(count)},
                             {"zoom", zoom}});
  }
};
extern "C" __attribute__((visibility("default"))) uintptr_t
nc_view_create(void *module, uintptr_t parent, const char *identity) {
  try {
    return static_cast<PdfModule *>(module)->create(parent, identity);
  } catch (...) {
    return 0;
  }
}
extern "C" __attribute__((visibility("default"))) void
nc_view_poll(void *module) {
  static_cast<PdfModule *>(module)->pollSafely();
}
