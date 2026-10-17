#include "audio.hpp"
#include <atomic>
#include <iostream>

namespace nc {
Module *make_module(nc_reply) { return nullptr; }
} // namespace nc
using namespace nc;
static void require(bool value, const char *message) {
  if (!value)
    throw std::runtime_error(message);
}
struct Result {
  int status;
  Value value;
};
static std::mutex repliesGate;
static std::map<uint64_t, Result> replies;
static void receive(uint64_t id, int status, const uint8_t *bytes,
                    size_t size) {
  std::lock_guard lock(repliesGate);
  require(
      replies.emplace(id, Result{status, Reader(bytes, size).read()}).second,
      "duplicate reply");
}
template <class F> static void until(F ready) {
  auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(3);
  while (!ready()) {
    require(std::chrono::steady_clock::now() < deadline, "timeout");
    std::this_thread::sleep_for(std::chrono::milliseconds(5));
  }
}
static Result result(uint64_t id) {
  until([&] {
    std::lock_guard lock(repliesGate);
    return replies.count(id) != 0;
  });
  std::lock_guard lock(repliesGate);
  return replies.at(id);
}
struct State {
  std::mutex gate;
  Bytes bytes;
  std::atomic<int64_t> played{0};
  std::atomic<bool> disposed{false};
  size_t limit = 4800;
  bool fail = false;
};
struct Fake final : AudioDevice {
  std::shared_ptr<State> state;
  explicit Fake(std::shared_ptr<State> state) : state(std::move(state)) {}
  ~Fake() override { state->disposed = true; }
  int64_t played() override { return state->played; }
  size_t write(const uint8_t *bytes, size_t frames) override {
    if (state->fail)
      throw Error("audio_device_error", "Disconnected");
    auto count = std::min(frames, state->limit);
    std::lock_guard lock(state->gate);
    state->bytes.insert(state->bytes.end(), bytes, bytes + count * 2);
    return count;
  }
  void pause(bool) override {}
};
static Value args() { return Value::Map{{"session", Bytes(16, 1)}}; }
static Bytes pcm(size_t frames) {
  Bytes bytes(16 + frames * 2, 0);
  std::fill_n(bytes.begin(), 16, 1);
  return bytes;
}
static void control(Audio &audio, uint64_t id, const char *method) {
  audio.request(id, "plugin.maui.audio.control", method, args(), "en_US");
}
static void send(Audio &audio, uint64_t id, const Bytes &bytes) {
  audio.request(id, "plugin.maui.audio.pcm", "", bytes, "en_US");
}
static int ack(uint64_t id) { return result(id).value.get<Bytes>()->at(0); }
static int64_t buffered(uint64_t id) {
  return *result(id)
              .value.get<Value::Map>()
              ->at("bufferedFrames")
              .get<int32_t>();
}
int main() {
  try {
    // Partial writes retain exact PCM; native device occupancy remains part of
    // the bound.
    auto state = std::make_shared<State>();
    state->limit = 3;
    {
      Audio audio(receive, [=] { return std::make_unique<Fake>(state); });
      control(audio, 1, "open");
      auto bytes = pcm(7);
      for (size_t i = 16; i < bytes.size(); ++i)
        bytes[i] = uint8_t(i);
      send(audio, 2, bytes);
      require(ack(2) == 0, "accept PCM");
      until([&] {
        std::lock_guard lock(state->gate);
        return state->bytes.size() == 14;
      });
      {
        std::lock_guard lock(state->gate);
        require(state->bytes == Bytes(bytes.begin() + 16, bytes.end()),
                "partial PCM preserved");
      }
      control(audio, 3, "status");
      require(buffered(3) == 7, "device buffered frames counted");
      state->played = 7;
      control(audio, 4, "status");
      require(buffered(4) == 0, "native drain");
      audio.cancel(1);
      require(result(1).status == 0 && state->disposed,
              "lease cancellation closes device");
      send(audio, 5, bytes);
      require(ack(5) == 3, "stale PCM rejected");
    }
    // Full software queue, paused preservation, stop and destructor completion.
    state = std::make_shared<State>();
    state->limit = 0;
    {
      Audio audio(receive, [=] { return std::make_unique<Fake>(state); });
      control(audio, 10, "open");
      for (uint64_t id = 11; id < 16; ++id) {
        send(audio, id, pcm(4800));
        require(ack(id) == 0, "fill capacity");
      }
      send(audio, 16, pcm(1));
      require(ack(16) == 1, "backpressure");
      control(audio, 17, "pause");
      send(audio, 18, pcm(1));
      require(ack(18) == 2, "paused ack");
      control(audio, 19, "status");
      require(buffered(19) == 24000, "pause retains queue");
      control(audio, 20, "stop");
      require(result(10).status == 0 && state->disposed, "stop closes lease");
      require(state->bytes.empty(), "no discarded partial writes");
      control(audio, 21, "open");
    }
    require(result(21).status == 0, "destructor completes lease");
    // Native worker errors close leases and permit a replacement session.
    auto failed = std::make_shared<State>();
    failed->fail = true;
    auto replacement = std::make_shared<State>();
    int opened = 0;
    {
      Audio audio(receive, [&] {
        return std::make_unique<Fake>(opened++ ? replacement : failed);
      });
      control(audio, 30, "open");
      send(audio, 31, pcm(1));
      require(result(30).status == 1 && failed->disposed,
              "failure closes native lease");
      control(audio, 32, "open");
      send(audio, 33, pcm(1));
      require(ack(33) == 0, "restart after error");
    }
    // Wire roundtrip and malformed input are tested across the same codec as
    // the ABI.
    Value value = Value::Map{{"unicode", "€"},
                             {"bytes", pcm(4800)},
                             {"count", int64_t(9223372036854775807LL)}};
    auto wire = Writer().write(value);
    require(Writer().write(Reader(wire.data(), wire.size()).read()) == wire,
            "codec roundtrip");
    for (Bytes bad : {Bytes{}, Bytes{8, 255, 255, 255, 255, 255}, Bytes{0, 0},
                      Bytes{13, 1, 0, 0}}) {
      bool rejected = false;
      try {
        Reader(bad.data(), bad.size()).read();
      } catch (const Error &) {
        rejected = true;
      }
      require(rejected, "reject malformed codec input");
    }
    std::cout << "PASS C++ partial writes, backpressure, pause, cancellation, "
                 "disposal, error recovery and codec checks\n";
  } catch (const std::exception &error) {
    std::cerr << error.what() << '\n';
    return 1;
  }
}
