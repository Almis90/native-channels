#include "channel.hpp"
#include <chrono>
#include <condition_variable>
#include <memory>
#include <mutex>
#include <pulse/pulseaudio.h>
#include <thread>

namespace nc {
// One native worker observes the current default sink; no managed polling or
// volume writes.
class Volume final : public Module {
  std::mutex gate;
  std::condition_variable wake;
  std::map<uint64_t, double> listeners;
  bool stopping = false;
  pa_mainloop *loop = nullptr;
  pa_context *context = nullptr;
  std::thread worker;
  void disconnect() {
    if (context) {
      pa_context_disconnect(context);
      pa_context_unref(context);
      context = nullptr;
    }
    if (loop) {
      pa_mainloop_free(loop);
      loop = nullptr;
    }
  }
  template <class Ready> void pump(Ready ready) {
    auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(3);
    while (!ready()) {
      auto state = pa_context_get_state(context);
      if (!PA_CONTEXT_IS_GOOD(state) ||
          std::chrono::steady_clock::now() > deadline)
        throw Error("volume_unavailable",
                    "Cannot read the PulseAudio default output.");
      pa_mainloop_iterate(loop, 0, nullptr);
      std::this_thread::sleep_for(std::chrono::milliseconds(5));
    }
  }
  void connect() {
    loop = pa_mainloop_new();
    if (!loop)
      throw Error("volume_unavailable",
                  "Cannot create the PulseAudio main loop.");
    context = pa_context_new(pa_mainloop_get_api(loop),
                             "NativeChannels volume example");
    if (!context ||
        pa_context_connect(context, nullptr, PA_CONTEXT_NOFLAGS, nullptr) < 0)
      throw Error("volume_unavailable",
                  "Cannot connect to the user's PulseAudio server.");
    pump([&] { return pa_context_get_state(context) == PA_CONTEXT_READY; });
  }
  template <class Ready> void operation(pa_operation *op, Ready ready) {
    if (!op)
      throw Error("volume_unavailable", "The audio server rejected the query.");
    try {
      pump(ready);
    } catch (...) {
      pa_operation_cancel(op);
      pa_operation_unref(op);
      throw;
    }
    pa_operation_unref(op);
  }
  double read() {
    struct Server {
      bool done = false;
      std::string sink;
    } server;
    operation(pa_context_get_server_info(
                  context,
                  [](pa_context *, const pa_server_info *info, void *data) {
                    auto &result = *static_cast<Server *>(data);
                    if (info && info->default_sink_name)
                      result.sink = info->default_sink_name;
                    result.done = true;
                  },
                  &server),
              [&] { return server.done; });
    if (server.sink.empty())
      throw Error("volume_unavailable", "No default output device.");
    struct Sink {
      bool done = false;
      double volume = -1;
    } sink;
    operation(
        pa_context_get_sink_info_by_name(
            context, server.sink.c_str(),
            [](pa_context *, const pa_sink_info *info, int end, void *data) {
              auto &result = *static_cast<Sink *>(data);
              if (info)
                result.volume =
                    info->mute
                        ? 0
                        : std::clamp(double(pa_cvolume_avg(&info->volume)) /
                                         PA_VOLUME_NORM,
                                     0.0, 1.0);
              if (end)
                result.done = true;
            },
            &sink),
        [&] { return sink.done; });
    if (sink.volume < 0)
      throw Error("volume_unavailable",
                  "Cannot read the default output volume.");
    return sink.volume;
  }
  void run() {
    std::unique_lock lock(gate);
    while (!stopping) {
      wake.wait(lock, [&] { return stopping || !listeners.empty(); });
      if (stopping)
        break;
      lock.unlock();
      double level = 0;
      std::unique_ptr<Error> failure;
      try {
        if (!context)
          connect();
        level = read();
      } catch (const std::exception &error) {
        failure = std::make_unique<Error>("volume_unavailable", error.what());
        disconnect();
      }
      lock.lock();
      for (auto &[id, previous] : listeners) {
        if (failure)
          fail(id, *failure);
        else if (previous != level) {
          reply(id, level, 3);
          previous = level;
        }
      }
      if (failure)
        listeners.clear();
      wake.wait_for(lock, std::chrono::milliseconds(150),
                    [&] { return stopping || listeners.empty(); });
      if (listeners.empty()) {
        lock.unlock();
        disconnect();
        lock.lock();
      }
    }
    disconnect();
  }

public:
  explicit Volume(nc_reply reply) : Module(reply), worker([this] { run(); }) {}
  ~Volume() override {
    {
      std::lock_guard lock(gate);
      stopping = true;
    }
    wake.notify_all();
    worker.join();
    for (auto &[id, _] : listeners)
      reply(id);
  }
  void request(uint64_t id, const std::string &channel,
               const std::string &method, const Value &,
               const char *) override {
    if (channel != "plugin.maui.volume" || method != "listen") {
      reply(id, {}, 2);
      return;
    }
    {
      std::lock_guard lock(gate);
      listeners.emplace(id, -1);
    }
    wake.notify_all();
  }
  void cancel(uint64_t id) override {
    {
      std::lock_guard lock(gate);
      if (listeners.erase(id))
        reply(id);
    }
    wake.notify_all();
  }
};
Module *make_module(nc_reply reply) { return new Volume(reply); }
} // namespace nc
