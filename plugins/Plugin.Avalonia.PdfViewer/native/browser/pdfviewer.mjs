import { views, draw, open, destroyView } from './pdf-dom.mjs';
const identity = new URL(import.meta.url).searchParams.get('instance');
export function register(context) {
    const view = views.get(identity);
    if (!view) throw new Error('The PDF view has already been detached.');
    let operation = false;
    const state = () => ({ instanceId: identity, page: view.page, pageCount: view.document?.numPages ?? 0, zoom: view.zoom });
    const registration = context.registerMethodHandler(`nativechannels.view.${identity}`, async (method, value, signal) => {
        if (view.closed) throw new context.ChannelError('view_disposed', 'The PDF view is detached.');
        if (operation || view.busy) throw new context.ChannelError('pdf_busy', 'Wait for the current document operation.');
        operation = true;
        try {
            if (method === 'openBytes') {
                if (!(value instanceof Uint8Array) || !value.length || value.length > 8 * 1024 * 1024) throw new context.ChannelError('invalid_arguments', 'Select a PDF up to 8 MiB.');
                await open(view, value, signal);
            } else if (method === 'goToPage') {
                if (!Number.isInteger(value) || value < 1 || value > (view.document?.numPages ?? 0)) throw new context.ChannelError('invalid_page', 'Page is outside the document.');
                view.page = value;
            } else if (method === 'setZoom') {
                if (!view.document) throw new context.ChannelError('document_not_open', 'Open a PDF first.');
                if (!Number.isFinite(value) || value < 1 || value > 4) throw new context.ChannelError('invalid_zoom', 'Zoom must be between 1 and 4.');
                view.zoom = value;
            } else if (method === 'getState') return state();
            else return context.notImplemented;
            await draw(view); return state();
        } finally { operation = false; }
    });
    return () => { registration(); destroyView(identity); };
}
