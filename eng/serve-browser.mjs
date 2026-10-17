// Minimal local static host with explicit MIME types for WebAssembly and ES modules.
import http from 'node:http';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

export async function serveBrowser(directory, port = 0) {
    const root = path.resolve(directory);
    const types = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript',
        '.wasm': 'application/wasm', '.json': 'application/json', '.css': 'text/css',
        '.png': 'image/png', '.svg': 'image/svg+xml', '.woff2': 'font/woff2' };
    const server = http.createServer(async (request, response) => {
        try {
            const url = new URL(request.url, 'http://localhost');
            if (url.pathname === '/favicon.ico') { response.writeHead(204).end(); return; }
            if (!url.pathname.startsWith('/currency/')) { response.writeHead(404).end(); return; }
            const name = decodeURIComponent(url.pathname.slice('/currency/'.length)) || 'index.html';
            const file = path.resolve(root, name);
            if (!file.startsWith(root + path.sep)) { response.writeHead(403).end(); return; }
            const data = await readFile(file);
            response.writeHead(200, { 'Content-Type': types[path.extname(file)] ?? 'application/octet-stream',
                'Content-Length': data.length, 'Cache-Control': 'no-store' });
            response.end(data);
        } catch { response.writeHead(404).end(); }
    });
    await new Promise((resolve, reject) => { server.once('error', reject); server.listen(port, '127.0.0.1', resolve); });
    return { server, url: `http://127.0.0.1:${server.address().port}/currency/` };
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
    if (!process.argv[2]) throw new Error('Usage: node eng/serve-browser.mjs <published-wwwroot> [port]');
    const { url } = await serveBrowser(process.argv[2], Number(process.argv[3] ?? 8085));
    console.log(`Currency: ${url}`);
}
