package dev.nativechannels;
import org.jetbrains.annotations.NotNull;

public interface NativeModule {
    void register(@NotNull NativeBridge bridge);
}
