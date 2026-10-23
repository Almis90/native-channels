import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { setTimeout as delay } from 'node:timers/promises';
import { createSession, send, cancel, reply, dispose, loadModules, toBase64, Status } from '../../src/NativeChannels/Platforms/Browser/nativechannels.mjs';
import { encodeEnvelope, decodeEnvelope, encodeValue, decodeValue, ChannelError } from '../../src/NativeChannels/Platforms/Browser/json-codec.mjs';
import { register as registerCurrency } from '../../plugins/Plugin.Currency/native/browser/currency.mjs';

const bytes = value => new TextEncoder().encode(value);
const call = (method, args) => encodeEnvelope({ method, arguments: encodeValue(args) });
function harness() {
    const events = [];
    const receive = value => events.push(JSON.parse(value));
    receive.dispose = () => { receive.released = (receive.released ?? 0) + 1; };
    return { session: createSession(receive), events, receive };
}
async function request(h, channel, payload) {
    const id = crypto.randomUUID();
    send(h.session, id, channel, toBase64(payload));
    for (let count = 0; count < 100; count++) {
        const result = h.events.find(event => event.id === id);
        if (result) return result;
        await delay(1);
    }
    throw new Error('No reply');
}

test('raw bytes cross both directions without mutation or codec interpretation', async () => {
    const h = harness();
    h.session.context.registerBinaryHandler('echo', payload => payload);
    const value = Uint8Array.from([0, 255, 128, 1]);
    const result = await request(h, 'echo', value);
    assert.equal(result.status, Status.success);
    assert.equal(result.payload, toBase64(value));
    const inbound = h.session.context.send('managed', value);
    value[0] = 88;
    const event = h.events.at(-1);
    reply(h.session, event.id, Status.success, event.payload);
    assert.deepEqual((await inbound).payload, Uint8Array.from([0, 255, 128, 1]));
    await dispose(h.session);
});

test('unknown channels and handler failures have distinct transport statuses', async () => {
    const h = harness();
    h.session.context.registerBinaryHandler('bad', () => { throw new Error('private'); });
    assert.equal((await request(h, 'missing', bytes('x'))).status, Status.channelNotFound);
    assert.equal((await request(h, 'bad', bytes('x'))).status, Status.handlerError);
    await dispose(h.session);
});

test('cancellation finishes an uncooperative handler once and suppresses its late reply', async () => {
    const h = harness();
    let complete, signal;
    h.session.context.registerBinaryHandler('slow', (_, token) => { signal = token; return new Promise(resolve => complete = resolve); });
    send(h.session, 'one', 'slow', '');
    await delay(0);
    cancel(h.session, 'one');
    assert.equal(signal.aborted, true);
    complete(bytes('late'));
    await delay(0);
    assert.equal(h.events.length, 1);
    assert.equal(h.events[0].status, Status.cancelled);
    assert.equal(h.session.incoming.size, 0);
    await dispose(h.session);
});

test('JS cancellation notifies managed code and ignores late or duplicate replies', async () => {
    const h = harness();
    const controller = new AbortController();
    const result = h.session.context.send('managed', bytes('x'), controller.signal);
    const id = h.events[0].id;
    controller.abort();
    assert.equal((await result).status, Status.cancelled);
    assert.equal(h.events[1].kind, 'cancel');
    reply(h.session, id, Status.success, toBase64(bytes('late')));
    assert.equal(h.session.outgoing.size, 0);
    assert.equal(h.events.length, 2);
    await dispose(h.session);
});

test('a late cancelled reply cannot complete a reused request ID', async () => {
    const h = harness(), complete = [];
    h.session.context.registerBinaryHandler('slow', () => new Promise(resolve => complete.push(resolve)));
    send(h.session, 'reused', 'slow', '');
    await delay(0);
    cancel(h.session, 'reused');
    send(h.session, 'reused', 'slow', '');
    await delay(0);
    complete[0](bytes('old'));
    await delay(0);
    assert.equal(h.events.length, 1);
    complete[1](bytes('new'));
    await delay(0);
    assert.equal(h.events.length, 2);
    assert.equal(h.events[1].payload, toBase64(bytes('new')));
    await dispose(h.session);
});

test('pre-cancelled JS requests never reach managed code', async () => {
    const h = harness();
    assert.equal((await h.session.context.send('managed', bytes('x'), AbortSignal.abort())).status, Status.cancelled);
    assert.equal(h.events.length, 0);
    await dispose(h.session);
});

test('removing a handler cancels its pending work and allows re-registration', async () => {
    const h = harness();
    const remove = h.session.context.registerBinaryHandler('slow', () => new Promise(() => {}));
    send(h.session, 'one', 'slow', '');
    await delay(0);
    remove(); remove();
    assert.equal(h.events[0].status, Status.cancelled);
    h.session.context.registerBinaryHandler('slow', () => bytes('new'));
    assert.equal((await request(h, 'slow', bytes(''))).status, Status.success);
    await dispose(h.session);
});

test('sessions isolate registrations; disposal releases callbacks and both pending directions', async () => {
    const h = harness(), other = harness();
    h.session.context.registerBinaryHandler('slow', () => new Promise(() => {}));
    send(h.session, 'one', 'slow', '');
    const pending = h.session.context.send('managed', bytes('x'));
    await dispose(h.session); await dispose(h.session);
    assert.equal((await pending).status, Status.disposed);
    assert.equal(h.events.find(event => event.id === 'one').status, Status.disposed);
    assert.equal(h.receive.released, 1);
    assert.equal(h.session.handlers.size + h.session.incoming.size + h.session.outgoing.size, 0);
    assert.equal((await request(other, 'slow', bytes(''))).status, Status.channelNotFound);
    assert.equal((await h.session.context.send('managed', bytes(''))).status, Status.disposed);
    await dispose(other.session);
});

test('module initialization rollback releases prior registrations and receiver', async () => {
    const h = harness();
    const good = 'data:text/javascript,' + encodeURIComponent('export function register(c) { return c.registerBinaryHandler("echo", x => x); }');
    const bad = 'data:text/javascript,' + encodeURIComponent('export function register() { throw new Error("broken"); }');
    await assert.rejects(loadModules(h.session, [good, bad], 'https://example.test/'), /broken/);
    assert.equal(h.session.closed, true);
    assert.equal(h.session.handlers.size, 0);
    assert.equal(h.receive.released, 1);
});

test('cleanup runs in reverse order even if a disposer throws', async () => {
    const h = harness(), order = [];
    h.session.disposers.push(() => order.push(1), () => { order.push(2); throw new Error('cleanup'); }, () => order.push(3));
    await assert.rejects(dispose(h.session), AggregateError);
    assert.deepEqual(order, [3, 2, 1]);
    assert.equal(h.receive.released, 1);
});

test('in-flight module registration is disposed when it completes after shutdown', async () => {
    const h = harness();
    const module = 'data:text/javascript,' + encodeURIComponent('export async function register(c) { await new Promise(r => setTimeout(r, 10)); return () => { globalThis.__lateRelease = true; }; }');
    const ready = loadModules(h.session, [module], 'https://example.test/');
    await delay(1);
    await dispose(h.session);
    await ready;
    assert.equal(globalThis.__lateRelease, true);
    delete globalThis.__lateRelease;
});

test('tagged JSON preserves Int64 extremes, bytes, nested maps and values', () => {
    const value = { min: -9223372036854775808n, max: 9223372036854775807n,
        bytes: Uint8Array.from([0, 255]), nested: [null, true, '€', 42, 3.25] };
    const decoded = decodeValue(encodeValue(value));
    for (const key of Object.keys(value)) assert.deepEqual(decoded[key], value[key]);
    assert.equal(Object.getPrototypeOf(decoded), null);
    assert.deepEqual(encodeValue(1e20), ['f64', 1e20]);
    for (const invalid of [9223372036854775808n, Infinity, NaN, undefined, new Date()])
        assert.throws(() => encodeValue(invalid));
});

test('malformed, duplicate, invalid UTF-8 and oversized values are rejected', () => {
    for (const invalid of [['i64', '9223372036854775808'], ['i32', 2147483648], ['s', null], ['b', 1], ['n', 1], ['unknown', 0]])
        assert.throws(() => decodeValue(invalid));
    assert.throws(() => decodeEnvelope(bytes('{"v":1,"v":1}')), /Duplicate/);
    assert.throws(() => decodeEnvelope(bytes('{"v":1,"value":["map",{"x":0,"\\u0078":1}]}')), /Duplicate/);
    assert.throws(() => decodeEnvelope(Uint8Array.from([255])));
    assert.throws(() => toBase64(new Uint8Array(16 * 1024 * 1024 + 1)));
    let deep = null;
    for (let index = 0; index < 66; index++) deep = [deep];
    assert.throws(() => encodeValue(deep), /nesting/);
});

test('shared protocol fixtures match the managed and native codecs', async () => {
    const fixtures = JSON.parse(await readFile(new URL('../fixtures/values.json', import.meta.url), 'utf8'));
    for (const value of fixtures.valid) assert.equal(JSON.stringify(encodeValue(decodeValue(value))), JSON.stringify(value));
    for (const value of fixtures.invalid) assert.throws(() => decodeValue(value));
});

test('Currency uses Intl and preserves unsupported-code and method errors', async () => {
    const h = harness();
    registerCurrency(h.session.context);
    for (const code of ['USD', 'EUR', 'GBP', 'JPY']) {
        const response = await request(h, 'plugin.maui.currency', call('getSymbol', code));
        const result = decodeEnvelope(Uint8Array.from(atob(response.payload), c => c.charCodeAt(0)));
        const expected = new Intl.NumberFormat(navigator.languages, { style: 'currency', currency: code }).formatToParts(0).find(p => p.type === 'currency').value;
        assert.equal(decodeValue(result.value), expected);
    }
    const error = decodeEnvelope(Uint8Array.from(atob((await request(h, 'plugin.maui.currency', call('getSymbol', 'ZZZ'))).payload), c => c.charCodeAt(0)));
    assert.equal(error.code, 'invalid_currency_code');
    assert.equal(error.platform, 'browser');
    assert.equal(decodeValue(error.details), 'ZZZ');
    const missing = await request(h, 'plugin.maui.currency', call('missing', null));
    assert.equal(decodeEnvelope(Uint8Array.from(atob(missing.payload), c => c.charCodeAt(0))).status, 'not_implemented');
    await dispose(h.session);
});

test('structured errors are preserved and unexpected exception details stay private', async () => {
    const h = harness();
    h.session.context.registerMethodHandler('error', method => {
        if (method === 'known') throw new ChannelError('example', 'message', { count: 9223372036854775807n });
        throw new Error('secret');
    });
    for (const method of ['known', 'unknown']) {
        const response = await request(h, 'error', call(method, null));
        const result = decodeEnvelope(Uint8Array.from(atob(response.payload), c => c.charCodeAt(0)));
        assert.equal(result.code, method === 'known' ? 'example' : 'browser_error');
        assert.notEqual(result.message, 'secret');
        if (method === 'known') assert.equal(decodeValue(result.details).count, 9223372036854775807n);
    }
    await dispose(h.session);
});
