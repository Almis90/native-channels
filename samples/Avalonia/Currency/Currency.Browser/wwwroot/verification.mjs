// Sample-only contract checks. This module is loaded solely by VerifyTransportAsync.
import { encodeEnvelope, encodeValue } from './_content/NativeChannels/json-codec.mjs';

export function register(context) {
    const remove = [];
    let cancellations = 0;
    const call = (method, args = null) => encodeEnvelope({ method, arguments: encodeValue(args) });
    remove.push(context.registerMethodHandler('verify.methods', async (method, args, signal) => {
        switch (method) {
            case 'echo': return args;
            case 'error': throw new context.ChannelError('test_error', 'Structured browser error.', args);
            case 'slow':
                signal.addEventListener('abort', () => { cancellations++; }, { once: true });
                return new Promise(() => {});
            case 'cancellations': return cancellations;
            default: return context.notImplemented;
        }
    }));
    remove.push(context.registerBinaryHandler('verify.bytes', payload => payload));
    remove.push(context.registerBinaryHandler('verify.bytes-standard', payload => payload));
    remove.push(context.registerBinaryHandler('verify.callback', async (payload, signal) => {
        const result = await context.send('verify.managed', payload, signal);
        if (result.status !== 0) throw new Error('Managed callback failed.');
        return result.payload;
    }));
    remove.push(context.registerBinaryHandler('verify.cancel-managed', async () => {
        const controller = new AbortController();
        const pending = context.send('verify.managed-slow', call('wait'), controller.signal);
        // Let the managed handler start before requesting cancellation.
        const timer = setTimeout(() => controller.abort(), 50);
        try {
            const result = await pending;
            return encodeEnvelope({ status: 'success', value: encodeValue(result.status) });
        } finally { clearTimeout(timer); }
    }));
    return () => { for (const release of remove.reverse()) release(); };
}
