package dev.nativechannels;

import android.content.Context;

/** Explicitly registered adapter entrypoint. Invoked only on Android's main looper. */
public interface NativeViewFactory {
    NativeViewInstance create(Context context, NativeBridge bridge, String instanceId,
                              String channelName, byte[] arguments);
}
