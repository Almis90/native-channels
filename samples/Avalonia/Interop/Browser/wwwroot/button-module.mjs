import { ChannelError, notImplemented } from './_content/NativeChannels/json-codec.mjs';
import { getView, update } from './button-dom.mjs';
import { invoke, delay } from './callback.mjs';

const identity = new URL(import.meta.url).searchParams.get('instance');
export function register(context) {
    const view = getView(identity), channel = 'nativechannels.view.' + identity;
    const lifetime = new AbortController();
    async function tap(signal) {
        if (view.closed) throw new ChannelError('view_disposed', 'Native button was detached.');
        ++view.count; update(view);
        return await invoke(context, channel, 'clicked', { instanceId: identity, count: view.count }, signal);
    }
    const clicked = () => void tap(lifetime.signal).catch(error => {
        if (!view.closed && !lifetime.signal.aborted) console.error(error);
    });
    view.element.addEventListener('click', clicked);
    const unregister = context.registerMethodHandler(channel, async (method, args, signal) => {
        if (view.closed) throw new ChannelError('view_disposed', 'Native button was detached.');
        switch (method) {
            case 'getState': return { instanceId: identity, title: view.title, count: view.count, uiThread: typeof document !== 'undefined' };
            case 'setTitle':
                if (typeof args !== 'string') throw new ChannelError('invalid_arguments', 'Expected title');
                view.title = args; update(view); return null;
            case 'simulateTap': return await tap(signal);
            case 'wait': await delay(Infinity, signal); return null;
            default: return notImplemented;
        }
    });
    return () => { lifetime.abort(); unregister(); view.element.removeEventListener('click', clicked); };
}
