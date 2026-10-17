// NativeChannels v1 tagged JSON. BigInt carries Int64 without a Number conversion.
import { fromBase64, toBase64, validateName } from './nativechannels.mjs';
const encoder = new TextEncoder();
const decoder = new TextDecoder('utf-8', { fatal: true });
export const notImplemented = Symbol('notImplemented');

export class ChannelError extends Error {
    constructor(code, message, details = null, platform = 'browser') {
        super(message);
        this.code = code;
        this.details = details;
        this.platform = platform;
    }
}

export function encodeValue(value, depth = 0) {
    if (depth > 64) throw new TypeError('Value nesting limit exceeded.');
    if (value === null) return ['n'];
    switch (typeof value) {
        case 'boolean': return ['b', value];
        case 'string': return ['s', value];
        case 'bigint':
            if (value < -9223372036854775808n || value > 9223372036854775807n) throw new RangeError('Int64 overflow.');
            return ['i64', value.toString()];
        case 'number':
            if (!Number.isFinite(value)) throw new TypeError('Non-finite channel number.');
            return [Number.isInteger(value) && value >= -2147483648 && value <= 2147483647 ? 'i32' : 'f64', value];
        case 'object':
            if (value instanceof Uint8Array) return ['bytes', toBase64(value)];
            if (Array.isArray(value)) return ['list', value.map(item => encodeValue(item, depth + 1))];
            if (Object.getPrototypeOf(value) === Object.prototype || Object.getPrototypeOf(value) === null) {
                const map = Object.create(null);
                for (const [key, item] of Object.entries(value)) map[key] = encodeValue(item, depth + 1);
                return ['map', map];
            }
    }
    throw new TypeError('Unsupported channel value.');
}

export function decodeValue(value, depth = 0) {
    if (depth > 64 || !Array.isArray(value) || value.length !== (value[0] === 'n' ? 1 : 2))
        throw new TypeError('Invalid tagged value.');
    const [tag, item] = value;
    switch (tag) {
        case 'n': return null;
        case 'b': if (typeof item === 'boolean') return item; break;
        case 's': if (typeof item === 'string') return item; break;
        case 'i32': if (Number.isInteger(item) && item >= -2147483648 && item <= 2147483647) return item; break;
        case 'i64':
            if (typeof item === 'string' && /^[+-]?\d+$/u.test(item)) {
                const integer = BigInt(item);
                if (integer >= -9223372036854775808n && integer <= 9223372036854775807n) return integer;
            }
            break;
        case 'f64': if (typeof item === 'number' && Number.isFinite(item)) return item; break;
        case 'bytes': return fromBase64(item);
        case 'list': if (Array.isArray(item)) return item.map(entry => decodeValue(entry, depth + 1)); break;
        case 'map':
            if (item && typeof item === 'object' && !Array.isArray(item)) {
                const map = Object.create(null);
                for (const [key, entry] of Object.entries(item)) map[key] = decodeValue(entry, depth + 1);
                return map;
            }
    }
    throw new TypeError('Malformed tagged value.');
}

export function encodeEnvelope(envelope) {
    const bytes = encoder.encode(JSON.stringify({ ...envelope, v: 1 }));
    if (bytes.length > 16 * 1024 * 1024) throw new RangeError('Payload limit exceeded.');
    return bytes;
}

export function decodeEnvelope(payload) {
    if (!(payload instanceof Uint8Array) || payload.length > 16 * 1024 * 1024)
        throw new TypeError('Invalid payload.');
    const text = decoder.decode(payload);
    const root = JSON.parse(text);
    // JSON.parse silently overwrites duplicate keys. Reject them, including escaped aliases.
    const stack = [];
    for (const token of text.matchAll(/"(?:\\.|[^"\\])*"|[{}\[\],:]|[^\s{}\[\],:]+/gu)) {
        const value = token[0];
        if (value === '{' || value === '[') stack.push(value === '{' ? { keys: new Set(), key: true } : null);
        else if (value === '}' || value === ']') stack.pop();
        else if (value === ',' && stack.at(-1)) stack.at(-1).key = true;
        else if (value.startsWith('"') && stack.at(-1)?.key) {
            const frame = stack.at(-1);
            const key = JSON.parse(value);
            if (frame.keys.has(key)) throw new TypeError('Duplicate JSON property.');
            frame.keys.add(key);
            frame.key = false;
        }
        if (stack.length > 208) throw new TypeError('Envelope nesting limit exceeded.');
    }
    if (!root || Array.isArray(root) || root.v !== 1) throw new TypeError('Unsupported method envelope.');
    return root;
}

export function methodHandler(handler) {
    return async (payload, signal) => {
        const call = decodeEnvelope(payload);
        validateName(call.method);
        const args = decodeValue(call.arguments);
        try {
            const value = await handler(call.method, args, signal);
            return value === notImplemented ? encodeEnvelope({ status: 'not_implemented' })
                : encodeEnvelope({ status: 'success', value: encodeValue(value) });
        } catch (error) {
            if (signal.aborted) throw error;
            return encodeEnvelope({ status: 'error',
                code: error instanceof ChannelError ? error.code : 'browser_error',
                message: error instanceof ChannelError ? error.message : 'Browser method handler failed.',
                platform: error instanceof ChannelError ? error.platform : 'browser',
                details: encodeValue(error instanceof ChannelError ? error.details : null) });
        }
    };
}
