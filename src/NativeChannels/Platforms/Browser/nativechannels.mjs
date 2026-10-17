// Session-scoped, codec-independent byte transport. No application modules are global.
import { methodHandler, ChannelError, notImplemented } from './json-codec.mjs';
export const Status = Object.freeze({ success: 0, channelNotFound: 1, handlerError: 2, disposed: 3, cancelled: 4 });
const maximumPayloadBytes = 16 * 1024 * 1024;
const encoder = new TextEncoder();

export function validateName(name) {
    if (typeof name !== 'string' || !name.trim() || /[\x00-\x1f\x7f-\x9f]/u.test(name) || encoder.encode(name).length > 256)
        throw new TypeError('Invalid channel or method name.');
}

export function toBase64(bytes) {
    if (!(bytes instanceof Uint8Array) || bytes.length > maximumPayloadBytes)
        throw new TypeError('Expected a bounded Uint8Array payload.');
    let text = '';
    for (let offset = 0; offset < bytes.length; offset += 8192)
        text += String.fromCharCode(...bytes.subarray(offset, offset + 8192));
    return btoa(text);
}

export function fromBase64(text) {
    if (typeof text !== 'string' || text.length > Math.ceil(maximumPayloadBytes / 3) * 4)
        throw new TypeError('Invalid base64 payload.');
    const bytes = Uint8Array.from(atob(text), character => character.charCodeAt(0));
    if (bytes.length > maximumPayloadBytes) throw new TypeError('Payload limit exceeded.');
    return bytes;
}

function notify(session, event) {
    session.receive?.(JSON.stringify(event));
}

function finish(session, id, status, payload = '', expected) {
    const operation = session.incoming.get(id);
    if (!operation || (expected && operation !== expected)) return;
    session.incoming.delete(id);
    notify(session, { kind: 'reply', id, status, payload });
    if (status !== Status.success) operation.controller.abort();
}

export function createSession(receive) {
    if (typeof receive !== 'function') throw new TypeError('A managed receiver is required.');
    const session = { receive, closed: false, handlers: new Map(), incoming: new Map(), outgoing: new Map(), disposers: [], shutdown: null };
    session.context = Object.freeze({
        ChannelError,
        notImplemented,
        registerMethodHandler(channel, handler) {
            return session.context.registerBinaryHandler(channel, methodHandler(handler));
        },
        registerBinaryHandler(channel, handler) {
            validateName(channel);
            if (session.closed) throw new Error('Session is disposed.');
            if (typeof handler !== 'function' || session.handlers.has(channel)) throw new Error('Invalid or duplicate handler.');
            const registration = { handler };
            session.handlers.set(channel, registration);
            return () => {
                if (session.handlers.get(channel) !== registration) return;
                session.handlers.delete(channel);
                for (const [id, operation] of session.incoming)
                    if (operation.registration === registration) finish(session, id, Status.cancelled);
            };
        },
        send(channel, payload, signal) {
            validateName(channel);
            const encoded = toBase64(payload); // Copy before the asynchronous boundary.
            if (session.closed) return Promise.resolve({ status: Status.disposed, payload: new Uint8Array() });
            if (signal?.aborted) return Promise.resolve({ status: Status.cancelled, payload: new Uint8Array() });
            return new Promise(resolve => {
                const id = crypto.randomUUID();
                const abort = () => {
                    if (!session.outgoing.has(id)) return;
                    reply(session, id, Status.cancelled, '');
                    notify(session, { kind: 'cancel', id });
                };
                session.outgoing.set(id, { resolve, signal, abort });
                signal?.addEventListener('abort', abort, { once: true });
                notify(session, { kind: 'request', id, channel, payload: encoded });
            });
        }
    });
    return session;
}

export async function loadModules(session, urls, baseUri = document.baseURI) {
    try {
        for (const url of urls) {
            if (session.closed) return;
            const module = await import(new URL(url, baseUri).href);
            if (session.closed) return;
            if (typeof module.register !== 'function') throw new TypeError(`Missing register export: ${url}`);
            const release = await module.register(session.context);
            if (typeof release !== 'function') throw new TypeError(`register must return a disposer: ${url}`);
            if (session.closed) await release();
            else session.disposers.push(release);
        }
    } catch (error) {
        try { await dispose(session); }
        catch (cleanup) { throw new AggregateError([error, cleanup], 'Module initialization and cleanup failed.'); }
        throw error;
    }
}

export function send(session, id, channel, encoded) {
    if (session.closed) return;
    if (session.incoming.has(id)) throw new Error('Duplicate request ID.');
    const controller = new AbortController();
    const registration = session.handlers.get(channel);
    const operation = { controller, registration };
    session.incoming.set(id, operation);
    // Queue invocation in FIFO order; an async handler releases the queue at its first await.
    Promise.resolve().then(async () => {
        if (session.incoming.get(id) !== operation) return;
        if (!registration) { finish(session, id, Status.channelNotFound, '', operation); return; }
        try {
            validateName(channel);
            const result = await registration.handler(fromBase64(encoded), controller.signal);
            if (session.incoming.get(id) === operation) finish(session, id, Status.success, toBase64(result), operation);
        } catch {
            finish(session, id, controller.signal.aborted ? Status.cancelled : Status.handlerError, '', operation);
        }
    });
}

export function cancel(session, id) { finish(session, id, Status.cancelled); }

export function reply(session, id, status, encoded) {
    const operation = session.outgoing.get(id);
    if (!operation) return; // Ignore late or duplicate replies.
    let payload;
    try {
        if (!Number.isInteger(status) || status < 0 || status > 4) throw new TypeError('Invalid status.');
        payload = fromBase64(encoded);
    } catch { status = Status.handlerError; payload = new Uint8Array(); }
    session.outgoing.delete(id);
    operation.signal?.removeEventListener('abort', operation.abort);
    operation.resolve({ status, payload });
}

export function dispose(session) {
    if (session.shutdown) return session.shutdown;
    session.closed = true;
    for (const id of session.incoming.keys()) finish(session, id, Status.disposed);
    for (const id of session.outgoing.keys()) {
        reply(session, id, Status.disposed, '');
        notify(session, { kind: 'cancel', id });
    }
    session.handlers.clear();
    const receive = session.receive;
    session.receive = null;
    // .NET's marshalled delegate exposes dispose; release its GCHandle deterministically.
    receive?.dispose?.();
    const disposers = session.disposers.splice(0).reverse();
    session.shutdown = Promise.resolve().then(async () => {
        const errors = [];
        for (const release of disposers) {
            try { await release(); } catch (error) { errors.push(error); }
        }
        if (errors.length) throw new AggregateError(errors, 'Browser adapter cleanup failed.');
    });
    return session.shutdown;
}
