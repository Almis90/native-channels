#pragma once
#include "channel.hpp"
#include <array>
#include <chrono>
#include <condition_variable>
#include <deque>
#include <functional>
#include <memory>
#include <mutex>
#include <thread>

namespace nc {
struct AudioDevice {
  virtual ~AudioDevice() = default;
  virtual int64_t played() = 0;
  virtual size_t write(const uint8_t *bytes, size_t frames) = 0;
  virtual void pause(bool paused) = 0;
};
class Audio final : public Module {
  struct Chunk {
    Bytes bytes;
    size_t offset = 0;
  };
  std::mutex gate;
  std::condition_variable wake;
  std::function<std::unique_ptr<AudioDevice>()> create;
  std::unique_ptr<AudioDevice> device;
  std::deque<Chunk> queue;
  Bytes identity;
  uint64_t lease = 0;
  int64_t accepted = 0, played = 0;
  bool paused = false, quitting = false;
  std::unique_ptr<Error> failure;
  std::thread worker;

  void progress() {
    if (device && !paused)
      played = device->played();
  }
  void close(const Error *error = nullptr) {
    device.reset();
    queue.clear();
    identity.clear();
    accepted = played = 0;
    paused = false;
    auto pending = lease;
    lease = 0;
    if (pending) {
      if (error)
        fail(pending, *error);
      else
        reply(pending);
    }
  }
  uint8_t write(const Value &value) {
    auto bytes = value.get<Bytes>();
    if (!bytes || bytes->size() < 18 || bytes->size() > 9616 ||
        bytes->size() % 2)
      return 4;
    if (!device ||
        !std::equal(identity.begin(), identity.end(), bytes->begin()))
      return 3;
    if (paused)
      return 2;
    progress();
    auto frames = (bytes->size() - 16) / 2;
    if (accepted - played + int64_t(frames) > 24000)
      return 1;
    queue.push_back({Bytes(bytes->begin() + 16, bytes->end()), 0});
    accepted += frames;
    return 0;
  }
  void pump() {
    std::unique_lock lock(gate);
    while (!quitting) {
      wake.wait_for(lock, std::chrono::milliseconds(5),
                    [&] { return quitting; });
      if (quitting)
        break;
      if (!device || paused)
        continue;
      try {
        progress();
        for (int i = 0; i < 64 && !queue.empty(); ++i) {
          auto &chunk = queue.front();
          auto count = device->write(chunk.bytes.data() + chunk.offset * 2,
                                     chunk.bytes.size() / 2 - chunk.offset);
          if (!count)
            break;
          chunk.offset += count;
          if (chunk.offset == chunk.bytes.size() / 2)
            queue.pop_front();
        }
      } catch (const Error &error) {
        failure = std::make_unique<Error>(error);
        close(failure.get());
      } catch (const std::exception &error) {
        failure = std::make_unique<Error>("audio_device_error", error.what());
        close(failure.get());
      }
    }
  }

public:
  Audio(nc_reply reply, std::function<std::unique_ptr<AudioDevice>()> create)
      : Module(reply), create(std::move(create)), worker([this] { pump(); }) {}
  ~Audio() override {
    {
      std::lock_guard lock(gate);
      quitting = true;
    }
    wake.notify_all();
    worker.join();
    close();
  }
  void cancel(uint64_t id) override {
    std::lock_guard lock(gate);
    if (lease == id)
      close();
  }
  void request(uint64_t id, const std::string &channel,
               const std::string &method, const Value &value,
               const char *) override {
    std::lock_guard lock(gate);
    if (channel == "plugin.maui.audio.pcm") {
      reply(id, Bytes{write(value)});
      return;
    }
    if (channel != "plugin.maui.audio.control") {
      reply(id, {}, 2);
      return;
    }
    auto map = value.get<Value::Map>();
    auto entry = map ? map->find("session") : Value::Map::const_iterator{};
    auto session =
        map && entry != map->end() ? entry->second.get<Bytes>() : nullptr;
    if (!session || session->size() != 16)
      throw Error("invalid_arguments", "Supply a 16-byte session identity.");
    if (method == "open") {
      if (device)
        throw Error("audio_busy", "An audio session is already open.");
      auto opened = create();
      failure.reset();
      device = std::move(opened);
      identity = *session;
      accepted = played = 0;
      paused = false;
      lease = id;
      return; // Resource lease is completed by stop, cancellation, failure or
              // destruction.
    }
    if (!identity.empty() && identity != *session)
      throw Error("audio_session_mismatch",
                  "This session is no longer current.");
    if (method == "stop") {
      close();
      reply(id);
      return;
    }
    if (failure)
      throw *failure;
    if (method == "status") {
      progress();
      reply(id, Value::Map{{"state", !device  ? "stopped"
                                     : paused ? "paused"
                                              : "playing"},
                           {"bufferedFrames", int32_t(accepted - played)},
                           {"capacityFrames", int32_t(24000)},
                           {"playedFrames", played}});
    } else if (method == "pause" || method == "resume") {
      if (!device)
        throw Error("audio_closed", "The audio session is closed.");
      bool requested = method == "pause";
      if (requested != paused) {
        device->pause(requested);
        paused = requested;
      }
      reply(id);
    } else
      reply(id, {}, 2);
  }
};
} // namespace nc
