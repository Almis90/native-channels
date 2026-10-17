// Browsers do not expose OS output volume. The plugin reports this capability honestly.
export function register(context) {
    const system = context.registerEventHandler('plugin.maui.volume', async function* () {
        throw new context.ChannelError('volume_unsupported', 'This browser cannot observe system output volume. Use the separate page-volume example.');
    });
    // Explicitly separate example: an HTMLMediaElement's own volume, never presented as OS state.
    const media = document.createElement('audio');
    media.volume = 0.5;
    const commands = context.registerMethodHandler('example.browser.volume.control', (method, level) => {
        if (method !== 'setVolume') return context.notImplemented;
        if (typeof level !== 'number' || !Number.isFinite(level) || level < 0 || level > 1)
            throw new context.ChannelError('invalid_volume', 'Volume must be between zero and one.');
        media.volume = level; return null;
    });
    const events = context.registerEventHandler('example.browser.volume', async function* (_, signal) {
        let wake = null, pending = true;
        const changed = () => { pending = true; wake?.(); wake = null; };
        const abort = () => { wake?.(); wake = null; };
        media.addEventListener('volumechange', changed); signal.addEventListener('abort', abort, { once: true });
        try {
            while (!signal.aborted) {
                if (!pending) await new Promise(resolve => { wake = resolve; if (signal.aborted) abort(); });
                if (signal.aborted) break;
                pending = false; yield media.volume;
            }
        } finally { media.removeEventListener('volumechange', changed); signal.removeEventListener('abort', abort); }
    });
    return () => { system(); commands(); events(); media.remove(); };
}
