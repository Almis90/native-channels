package dev.nativechannels;
import org.jetbrains.annotations.NotNull;

public interface MessageHandler {
    void onMessage(@NotNull String channel, @NotNull byte[] payload, @NotNull Reply reply);
}
