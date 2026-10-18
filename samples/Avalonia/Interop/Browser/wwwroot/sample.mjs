import { ChannelError, notImplemented } from './_content/NativeChannels/json-codec.mjs';
import { invoke, delay } from './callback.mjs';

export function register(context) {
    let active = 0, next = 0;
    const registrations = [
        context.registerMethodHandler('sample', async (method, args, signal) => {
            switch (method) {
                case 'add': return args.a + args.b;
                case 'device': return { platform: 'Browser', name: navigator.userAgent, id: 9223372036854775807n };
                case 'echo': return args;
                case 'null': return null;
                case 'callback': return 'native:' + await invoke(context, 'sample', 'getToken', null, signal);
                case 'delay': return await delay(args, signal);
                case 'fail': throw new ChannelError('sample_error', 'Browser adapter error');
                case 'activeStreams': return active;
                default: return notImplemented;
            }
        }),
        context.registerMethodHandler('sample.ui', method => method === 'isMainThread' ? typeof document !== 'undefined' : notImplemented),
        context.registerEventHandler('sample.events', async function* (mode, signal) {
            ++active;
            try {
                if (mode === 'finite') { yield null; yield 42; }
                else if (mode === 'error') { yield 1; throw new ChannelError('sample_stream_error', 'Browser stream error', 42); }
                else { yield ++next; await delay(Infinity, signal); }
            } finally { --active; }
        }),
    ];
    // Forward the opaque codec payload, including arbitrary StandardMessageCodec map keys and typed arrays.
    for (const codec of ['json', 'string', 'binary', 'standard']) {
        const name = 'sample.messages.' + codec;
        registrations.push(context.registerBinaryHandler(name, async (payload, signal) => {
            const reply = await context.send(name, payload, signal);
            if (reply.status !== 0) throw new ChannelError('callback_failed', 'Managed message handler failed.');
            return reply.payload;
        }));
    }
    return () => registrations.forEach(dispose => dispose());
}
