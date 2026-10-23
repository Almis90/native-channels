// Web Audio owns playback and scheduling. The facade sends the same PCM protocol as native hosts.
export function register(context) {
    let audio = null, current = null, disposed = false;
    const nodes = new Set();
    const equal = (a, b) => a instanceof Uint8Array && b instanceof Uint8Array && a.length === b.length && a.every((v, i) => v === b[i]);
    const error = (code, message) => new context.ChannelError(code, message);
    // Capture a real user gesture before the async .NET/channel hop loses activation.
    const unlock = () => {
        if (disposed || current?.paused) return;
        audio ??= new AudioContext({ sampleRate: 48000 });
        void audio.resume().catch(() => {});
    };
    document.addEventListener('pointerdown', unlock, true);
    document.addEventListener('keydown', unlock, true);
    function stop(reason) {
        const old = current;
        current = null;
        for (const node of nodes) { node.onended = null; try { node.stop(); } catch {} node.disconnect(); }
        nodes.clear();
        if (old) { old.signal.removeEventListener('abort', old.abort); reason ? old.reject(reason) : old.resolve(null); }
    }
    const hide = () => { if (document.hidden) stop(error('audio_interrupted', 'Playback stopped because the page was hidden.')); };
    const close = () => stop(error('audio_interrupted', 'The page is closing.'));
    document.addEventListener('visibilitychange', hide);
    globalThis.addEventListener('pagehide', close);
    const control = context.registerMethodHandler('plugin.maui.audio.control', async (method, args, signal) => {
        const id = args?.session;
        if (!(id instanceof Uint8Array) || id.length !== 16) throw error('invalid_arguments', 'Supply a 16-byte session identity.');
        if (method === 'open') {
            if (current) throw error('audio_busy', 'An audio session is already open.');
            if (!audio || audio.state !== 'running') throw error('audio_user_activation_required', 'Click Play to enable browser audio.');
            return new Promise((resolve, reject) => {
                const session = { id: id.slice(), signal, resolve, reject, accepted: 0, played: 0, paused: false, next: audio.currentTime,
                    abort: () => { if (current === session) stop(); } };
                current = session;
                signal.addEventListener('abort', session.abort, { once: true });
                if (signal.aborted) session.abort();
            });
        }
        if (current && !equal(id, current.id)) throw error('audio_session_mismatch', 'This session is no longer current.');
        if (method === 'stop') { stop(); return null; }
        if (method === 'status') return {
            state: !current ? 'stopped' : current.paused ? 'paused' : 'playing',
            bufferedFrames: current ? current.accepted - current.played : 0,
            capacityFrames: 24000, playedFrames: BigInt(current?.played ?? 0)
        };
        if (method === 'pause' || method === 'resume') {
            if (!current) throw error('audio_closed', 'The audio session is closed.');
            const session = current;
            if (method === 'pause') { await audio.suspend(); if (current === session) session.paused = true; }
            else { await audio.resume(); if (current === session) session.paused = false; }
            return null;
        }
        return context.notImplemented;
    });
    const data = context.registerBinaryHandler('plugin.maui.audio.pcm', payload => {
        // BinaryMessageCodec wraps non-null bytes with a one-byte presence marker.
        const reply = status => new Uint8Array([1, status]);
        if (payload[0] !== 1) return reply(4);
        const bytes = payload.subarray(1);
        if (bytes.length < 18 || bytes.length > 9616 || bytes.length % 2) return reply(4);
        if (!current || !equal(bytes.subarray(0, 16), current.id)) return reply(3);
        if (current.paused || audio.state !== 'running') return reply(2);
        const frames = (bytes.length - 16) / 2;
        if (current.accepted - current.played + frames > 24000) return reply(1);
        const session = current;
        const buffer = audio.createBuffer(1, frames, 48000), output = buffer.getChannelData(0);
        const pcm = new DataView(bytes.buffer, bytes.byteOffset + 16, bytes.length - 16);
        for (let i = 0; i < frames; i++) output[i] = pcm.getInt16(i * 2, true) / 32768;
        const node = audio.createBufferSource(); node.buffer = buffer; node.connect(audio.destination);
        node.onended = () => { nodes.delete(node); node.disconnect(); if (current === session) session.played += frames; };
        nodes.add(node);
        const start = Math.max(session.next, audio.currentTime);
        node.start(start); session.next = start + frames / 48000; session.accepted += frames;
        return reply(0);
    });
    return async () => {
        disposed = true; stop(); control(); data();
        document.removeEventListener('pointerdown', unlock, true); document.removeEventListener('keydown', unlock, true);
        document.removeEventListener('visibilitychange', hide); globalThis.removeEventListener('pagehide', close);
        if (audio) await audio.close(); audio = null;
    };
}
