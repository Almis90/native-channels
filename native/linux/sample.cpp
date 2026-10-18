#include "channel.hpp"
#include <X11/Xlib.h>
#include <chrono>
#include <condition_variable>
#include <gdk/gdkx.h>
#include <gtk/gtk.h>
#include <limits>
#include <mutex>
#include <set>
#include <thread>

using nc::Value;
class Sample final : public nc::Module {
  std::mutex gate;
  std::condition_variable changed;
  bool stopping = false;
  std::thread worker;
  const std::thread::id ui = std::this_thread::get_id();
  struct Delay {
    std::chrono::steady_clock::time_point due;
    int32_t value;
  };
  std::map<uint64_t, Delay> delays;
  std::map<uint64_t, std::string> callbacks;
  std::set<uint64_t> streams, waiting;
  int32_t nextStream = 0, count = 0;
  uint64_t nextClick = uint64_t(1) << 63;
  GtkWidget *window = nullptr, *button = nullptr;
  std::string identity, title;
  static int32_t integer(const Value &v) {
    if (auto p = v.get<int32_t>())
      return *p;
    throw nc::Error("invalid_arguments", "Expected Int32");
  }
  void work() {
    std::unique_lock<std::mutex> lock(gate);
    while (!stopping) {
      auto next = std::chrono::steady_clock::time_point::max();
      for (auto it = delays.begin(); it != delays.end();) {
        if (it->second.due <= std::chrono::steady_clock::now()) {
          auto id = it->first;
          auto value = it->second.value;
          it = delays.erase(it);
          reply(id, value);
        } else {
          next = std::min(next, it->second.due);
          ++it;
        }
      }
      changed.wait_until(lock, next);
    }
  }
  void update() {
    gtk_button_set_label(
        GTK_BUTTON(button),
        (title + " · " + std::to_string(count) + " taps").c_str());
  }
  void tap(uint64_t id, bool unsolicited = false) {
    ++count;
    update();
    callbacks[id] = unsolicited ? "click" : "tap";
    reply(id,
          Value::Map{{"channel", "nativechannels.view." + identity},
                     {"method", "clicked"},
                     {"arguments",
                      Value::Map{{"instanceId", identity}, {"count", count}}}},
          4);
  }

public:
  explicit Sample(nc_reply callback) : Module(callback) {
    worker = std::thread([this] { work(); });
  }
  ~Sample() override {
    {
      std::lock_guard<std::mutex> lock(gate);
      stopping = true;
      changed.notify_all();
    }
    worker.join();
    for (const auto &[id, delay] : delays) {
      (void)delay;
      reply(id);
    }
    for (auto id : streams)
      reply(id);
    for (auto id : waiting)
      reply(id);
    for (const auto &[id, kind] : callbacks)
      if (kind != "click")
        reply(id);
    if (window)
      gtk_widget_destroy(window);
  }
  void request(uint64_t id, const std::string &channel,
               const std::string &method, const Value &value,
               const char *) override {
    std::lock_guard<std::mutex> lock(gate);
    if (channel == "sample.events") {
      auto mode = value.get<std::string>();
      if (mode && *mode == "finite") {
        reply(id, {}, 3);
        reply(id, int32_t(42), 3);
        reply(id);
      } else if (mode && *mode == "error") {
        reply(id, int32_t(1), 3);
        fail(id, nc::Error("sample_stream_error", "Linux stream error",
                           int32_t(42)));
      } else {
        streams.insert(id);
        reply(id, ++nextStream, 3);
      }
    } else if (channel.rfind("sample.messages.", 0) == 0) {
      callbacks[id] = "raw";
      reply(id, Value::Map{{"channel", channel}, {"payload", value}}, 4);
    } else if (channel == "sample.ui" && method == "isMainThread")
      reply(id, std::this_thread::get_id() == ui);
    else if (channel == "sample") {
      if (method == "echo")
        reply(id, value);
      else if (method == "null")
        reply(id);
      else if (method == "add") {
        auto map = value.get<Value::Map>();
        if (!map)
          throw nc::Error("invalid_arguments", "Expected arguments");
        auto sum = int64_t(integer(map->at("a"))) + integer(map->at("b"));
        if (sum < INT32_MIN || sum > INT32_MAX)
          throw nc::Error("invalid_arguments", "Sum overflow");
        reply(id, int32_t(sum));
      } else if (method == "device")
        reply(id, Value::Map{{"platform", "Linux"},
                             {"name", g_get_host_name()},
                             {"id", std::numeric_limits<int64_t>::max()}});
      else if (method == "callback") {
        callbacks[id] = "prefix";
        reply(id,
              Value::Map{{"channel", "sample"},
                         {"method", "getToken"},
                         {"arguments", {}}},
              4);
      } else if (method == "delay") {
        auto ms = integer(value);
        if (ms < 0)
          throw nc::Error("invalid_arguments", "Negative delay");
        delays[id] = {std::chrono::steady_clock::now() +
                          std::chrono::milliseconds(ms),
                      ms};
        changed.notify_all();
      } else if (method == "activeStreams")
        reply(id, int32_t(streams.size()));
      else if (method == "fail")
        fail(id, nc::Error("sample_error", "Linux adapter error"));
      else
        reply(id, {}, 2);
    } else if (button && channel == "nativechannels.view." + identity) {
      if (method == "getState")
        reply(id, Value::Map{{"instanceId", identity},
                             {"title", title},
                             {"count", count},
                             {"uiThread", std::this_thread::get_id() == ui}});
      else if (method == "setTitle") {
        auto text = value.get<std::string>();
        if (!text)
          throw nc::Error("invalid_arguments", "Expected title");
        title = *text;
        update();
        reply(id);
      } else if (method == "simulateTap")
        tap(id);
      else if (method == "wait")
        waiting.insert(id);
      else
        reply(id, {}, 2);
    } else
      reply(id, {}, 2);
  }
  void cancel(uint64_t id) override {
    std::lock_guard<std::mutex> lock(gate);
    auto removed = delays.erase(id) + streams.erase(id) + callbacks.erase(id) +
                   waiting.erase(id);
    if (removed)
      reply(id);
    changed.notify_all();
  }
  void response(uint64_t id, int status, const Value &value) {
    std::lock_guard<std::mutex> lock(gate);
    auto it = callbacks.find(id);
    if (it == callbacks.end())
      return;
    auto kind = it->second;
    callbacks.erase(it);
    if (kind == "click")
      return;
    if (status == 0 && kind == "prefix") {
      auto text = value.get<std::string>();
      if (!text) {
        fail(id, nc::Error("callback_failed", "Expected token"));
        return;
      }
      reply(id, "native:" + *text);
    } else
      reply(id, value, status);
  }
  uintptr_t create(uintptr_t parent, const char *id, const Value &arguments) {
    if (window || !gtk_init_check(nullptr, nullptr) ||
        !GDK_IS_X11_DISPLAY(gdk_display_get_default()))
      throw nc::Error("view_host_failed", "GTK buttons require X11");
    identity = id;
    auto options = arguments.get<Value::Map>();
    auto text = options ? options->at("title").get<std::string>() : nullptr;
    if (!text)
      throw nc::Error("invalid_arguments", "Expected title");
    title = *text;
    window = gtk_window_new(GTK_WINDOW_TOPLEVEL);
    gtk_window_set_decorated(GTK_WINDOW(window), FALSE);
    button = gtk_button_new();
    update();
    gtk_container_add(GTK_CONTAINER(window), button);
    g_signal_connect(button, "clicked",
                     G_CALLBACK(+[](GtkButton *, gpointer data) {
                       auto self = static_cast<Sample *>(data);
                       std::lock_guard<std::mutex> lock(self->gate);
                       self->tap(++self->nextClick, true);
                     }),
                     this);
    gtk_widget_realize(window);
    auto native = gtk_widget_get_window(window);
    auto display = GDK_WINDOW_XDISPLAY(native);
    auto xid = GDK_WINDOW_XID(native);
    XReparentWindow(display, xid, parent, 0, 0);
    gtk_widget_show_all(window);
    XFlush(display);
    return xid;
  }
  void poll() {
    for (int i = 0; i < 16 && gtk_events_pending(); ++i)
      gtk_main_iteration_do(FALSE);
  }
};
namespace nc {
Module *make_module(nc_reply callback) { return new Sample(callback); }
} // namespace nc
extern "C" void nc_response(void *module, uint64_t id, int status,
                            const uint8_t *data, size_t size) {
  auto self = static_cast<Sample *>(module);
  try {
    self->response(id, status, nc::Reader(data, size).read());
  } catch (const std::exception &error) {
    self->fail(id, nc::Error("callback_failed", error.what()));
  }
}
extern "C" uintptr_t nc_view_create_configured(void *module, uintptr_t parent,
                                               const char *id,
                                               const uint8_t *data,
                                               size_t size) {
  try {
    return static_cast<Sample *>(module)->create(parent, id,
                                                 nc::Reader(data, size).read());
  } catch (...) {
    return 0;
  }
}
extern "C" void nc_view_poll(void *module) {
  static_cast<Sample *>(module)->poll();
}
