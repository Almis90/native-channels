package dev.nativechannels;
import org.jetbrains.annotations.NotNull;
public interface RequestHandler {
    void onRequest(@NotNull String channel, @NotNull byte[] payload, @NotNull RequestContext context);
}
