const views = new Map();
export function createView(identity, title) {
    if (views.has(identity)) throw new Error('Duplicate native view identity.');
    const element = document.createElement('button');
    element.style.cssText = 'width:100%;height:100%;font:inherit;cursor:pointer;';
    const view = { identity, title, count: 0, element, closed: false };
    views.set(identity, view);
    update(view);
    return element;
}
export function update(view) { view.element.textContent = `${view.title} · ${view.count} taps`; }
export function getView(identity) {
    const view = views.get(identity);
    if (!view || view.closed) throw new Error('Native button has been detached.');
    return view;
}
export function destroyView(identity) {
    const view = views.get(identity);
    if (!view) return;
    view.closed = true;
    view.element.remove();
    views.delete(identity);
}
