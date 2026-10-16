package dev.nativechannels;
import org.jetbrains.annotations.NotNull;

public interface Reply {
    void complete(int status, @NotNull byte[] payload);
}
