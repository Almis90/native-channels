package dev.nativechannels;

import android.view.View;

/** One native view and its resources. close must detach listeners and release SDK resources. */
public interface NativeViewInstance {
    View getView();
    void close();
}
