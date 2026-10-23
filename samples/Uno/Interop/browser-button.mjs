import { createView, destroyView } from './button-dom.mjs';
export { destroyView };
export function createElement(identity, title, parentId) {
    document.getElementById(parentId).append(createView(identity, title));
}
