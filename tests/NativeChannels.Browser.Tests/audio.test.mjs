import test from 'node:test';
import assert from 'node:assert/strict';
import { register } from '../../plugins/Plugin.AudioStreamer/native/browser/audiostreamer.mjs';

test('PCM acknowledgements preserve BinaryMessageCodec framing, including errors', async () => {
    const previous = globalThis.document;
    const previousAdd = globalThis.addEventListener;
    const previousRemove = globalThis.removeEventListener;
    globalThis.document = { addEventListener() {}, removeEventListener() {} };
    globalThis.addEventListener = globalThis.removeEventListener = () => {};
    let handler;
    try {
        const dispose = register({ ChannelError: Error, registerMethodHandler: () => () => {},
            registerBinaryHandler: (_, callback) => { handler = callback; return () => {}; } });
        assert.deepEqual(handler(new Uint8Array([0])), new Uint8Array([1, 4]), 'null is invalid PCM, not a null acknowledgement');
        assert.deepEqual(handler(new Uint8Array([1])), new Uint8Array([1, 4]), 'empty PCM reports invalid arguments');
        const frame = new Uint8Array(19); // marker, 16-byte session identity, one 16-bit frame
        frame[0] = 1;
        assert.deepEqual(handler(frame), new Uint8Array([1, 3]), 'valid frame for a closed session reports closed');
        await dispose();
    } finally {
        globalThis.document = previous;
        globalThis.addEventListener = previousAdd;
        globalThis.removeEventListener = previousRemove;
    }
});
