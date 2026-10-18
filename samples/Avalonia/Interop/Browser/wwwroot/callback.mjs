import { ChannelError, encodeEnvelope, decodeEnvelope, encodeValue, decodeValue } from './_content/NativeChannels/json-codec.mjs';

export async function invoke(context, channel, method, args, signal) {
    const reply = await context.send(channel, encodeEnvelope({ method, arguments: encodeValue(args) }), signal);
    if (reply.status !== 0) throw new ChannelError('callback_failed', 'Managed callback was unavailable.');
    const envelope = decodeEnvelope(reply.payload);
    if (envelope.status !== 'success')
        throw new ChannelError(envelope.code ?? 'callback_failed', envelope.message ?? 'Managed callback failed.',
            envelope.details ? decodeValue(envelope.details) : null);
    return decodeValue(envelope.value);
}

export function delay(milliseconds, signal) {
    return new Promise((resolve, reject) => {
        signal.throwIfAborted();
        const abort = () => { clearTimeout(timer); signal.removeEventListener('abort', abort); reject(signal.reason); };
        const timer = milliseconds === Infinity ? null : setTimeout(() => {
            signal.removeEventListener('abort', abort); resolve(milliseconds);
        }, milliseconds);
        signal.addEventListener('abort', abort, { once: true });
    });
}
