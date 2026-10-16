package dev.nativechannels;
import org.jetbrains.annotations.NotNull;
public interface RequestContext extends Reply {
    @NotNull String getRequestId();
    boolean isCancelled();
    void onCancelled(@NotNull CancellationHandler handler);
}
