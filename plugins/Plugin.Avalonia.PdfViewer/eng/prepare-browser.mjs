import { cpSync, existsSync, mkdirSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { resolve } from 'node:path';
const root = fileURLToPath(new URL('../', import.meta.url));
const source = resolve(root, 'node_modules/pdfjs-dist');
if (!existsSync(resolve(source, 'build/pdf.mjs'))) throw new Error('Run npm ci in plugins/Plugin.Avalonia.PdfViewer before packing the browser target.');
const dest = resolve(root, 'native/browser/vendor');
mkdirSync(dest, { recursive: true });
for (const file of ['build/pdf.mjs', 'build/pdf.worker.mjs', 'cmaps', 'standard_fonts', 'wasm', 'LICENSE'])
    cpSync(resolve(source, file), resolve(dest, file.replace('build/', '')), { recursive: true });
