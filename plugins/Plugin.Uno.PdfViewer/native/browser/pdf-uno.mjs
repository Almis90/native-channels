import { createView, destroyView } from './pdf-dom.mjs';
export { destroyView };
export function createElement(identity, parentId) {
    document.getElementById(parentId).append(createView(identity));
}
