#include "audio.hpp"
#include <alsa/asoundlib.h>

namespace nc {
class Alsa final : public AudioDevice {
  snd_pcm_t *pcm = nullptr;
  int64_t written = 0, consumed = 0;
  bool hardwarePaused = false;
  static void check(int result, const char *operation) {
    if (result < 0)
      throw Error("audio_device_error", std::string("ALSA could not ") +
                                            operation + ": " +
                                            snd_strerror(result));
  }

public:
  Alsa() {
    check(snd_pcm_open(&pcm, "default", SND_PCM_STREAM_PLAYBACK,
                       SND_PCM_NONBLOCK),
          "open default output");
    try {
      check(snd_pcm_set_params(pcm, SND_PCM_FORMAT_S16_LE,
                               SND_PCM_ACCESS_RW_INTERLEAVED, 1, 48000, 1,
                               100000),
            "configure PCM");
    } catch (...) {
      snd_pcm_close(pcm);
      pcm = nullptr;
      throw;
    }
  }
  ~Alsa() override {
    if (pcm) {
      snd_pcm_drop(pcm);
      snd_pcm_close(pcm);
    }
  }
  int64_t played() override {
    if (hardwarePaused || written == consumed)
      return consumed;
    snd_pcm_sframes_t delay = 0;
    int result = snd_pcm_delay(pcm, &delay);
    if (result == -EPIPE || snd_pcm_state(pcm) == SND_PCM_STATE_XRUN) {
      consumed = written;
      check(snd_pcm_prepare(pcm), "recover underrun");
    } else {
      check(result, "read playback delay");
      consumed =
          std::max(consumed, std::clamp(written - delay, int64_t(0), written));
    }
    return consumed;
  }
  size_t write(const uint8_t *bytes, size_t frames) override {
    auto count = snd_pcm_writei(pcm, bytes, frames);
    if (count == -EAGAIN)
      return 0;
    if (count == -EPIPE) {
      consumed = written;
      check(snd_pcm_prepare(pcm), "recover underrun");
      return 0;
    }
    check(int(count), "write PCM");
    written += count;
    if (count > 0 && snd_pcm_state(pcm) == SND_PCM_STATE_PREPARED)
      check(snd_pcm_start(pcm), "start PCM");
    return size_t(count);
  }
  void pause(bool paused) override {
    if (paused) {
      played();
      if (written == consumed || snd_pcm_state(pcm) != SND_PCM_STATE_RUNNING)
        return;
      int result = snd_pcm_pause(pcm, 1);
      if (result == -ENOSYS)
        throw Error(
            "audio_pause_unsupported",
            "The default ALSA output does not support retaining paused PCM.");
      check(result, "pause PCM");
      hardwarePaused = true;
    } else if (hardwarePaused) {
      check(snd_pcm_pause(pcm, 0), "resume PCM");
      hardwarePaused = false;
    }
  }
};
Module *make_module(nc_reply reply) {
  return new Audio(reply, [] { return std::make_unique<Alsa>(); });
}
} // namespace nc
