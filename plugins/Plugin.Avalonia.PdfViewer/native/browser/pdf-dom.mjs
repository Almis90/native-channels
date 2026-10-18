import { getDocument, GlobalWorkerOptions } from './vendor/pdf.mjs';
GlobalWorkerOptions.workerSrc = new URL('./vendor/pdf.worker.mjs', import.meta.url).href;
export const views = new Map();
export function createView(identity) {
    const element = document.createElement('div');
    element.style.cssText = 'width:100%;height:100%;overflow:auto;background:#e2e8f0';
    const canvas = document.createElement('canvas'); element.append(canvas);
    const view = { element, canvas, document: null, page: 0, zoom: 1, closed: false, busy: false, render: null, loading: null, redraw: false };
    views.set(identity, view);
    view.observer = new ResizeObserver(() => { view.redraw = true; if (!view.busy && !view.loading) void draw(view).catch(() => {}); });
    view.observer.observe(element);
    return element;
}
export async function draw(view) {
    if (view.closed || !view.document || view.element.clientWidth === 0 || view.element.clientHeight === 0) return;
    if (view.busy) { view.redraw = true; return; }
    view.busy = true;
    try {
        do {
            view.redraw = false;
            const page = await view.document.getPage(view.page);
            if (view.closed) return;
            const base = page.getViewport({ scale: 1 });
            const scale = Math.min((view.element.clientWidth - 16) / base.width, (view.element.clientHeight - 16) / base.height) * view.zoom;
            const viewport = page.getViewport({ scale: Math.max(0.01, scale) });
            const ratio = Math.min(devicePixelRatio || 1, 2);
            view.canvas.width = Math.ceil(viewport.width * ratio); view.canvas.height = Math.ceil(viewport.height * ratio);
            view.canvas.style.width = `${viewport.width}px`; view.canvas.style.height = `${viewport.height}px`;
            view.render = page.render({ canvasContext: view.canvas.getContext('2d'), viewport, transform: [ratio, 0, 0, ratio, 0, 0] });
            await view.render.promise; view.render = null;
        } while (view.redraw && !view.closed);
    } finally { view.busy = false; }
}
export async function open(view, data, signal) {
    const loading = view.loading = getDocument({ data, isEvalSupported: false,
        cMapUrl: new URL('./vendor/cmaps/', import.meta.url).href, cMapPacked: true,
        standardFontDataUrl: new URL('./vendor/standard_fonts/', import.meta.url).href,
        wasmUrl: new URL('./vendor/wasm/', import.meta.url).href });
    loading.onPassword = () => { void loading.destroy(); };
    const abort = () => { void loading.destroy(); };
    signal.addEventListener('abort', abort, { once: true });
    try {
        const next = await loading.promise;
        if (signal.aborted || view.closed) { await next.destroy(); throw new DOMException('Cancelled', 'AbortError'); }
        const previous = view.document;
        view.document = next; view.page = 1; view.zoom = 1;
        await previous?.destroy();
    } finally { signal.removeEventListener('abort', abort); view.loading = null; }
}
export function destroyView(identity) {
    const view = views.get(identity); if (!view) return;
    views.delete(identity); view.closed = true; view.observer.disconnect();
    view.render?.cancel(); void view.loading?.destroy(); void view.document?.destroy(); view.element.remove();
}
