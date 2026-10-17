// Runs the actual published WebAssembly app, including the managed transport checks.
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { readFile, writeFile, mkdir, readdir, rm } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { createRequire } from 'node:module';
import { serveBrowser } from './serve-browser.mjs';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const require = createRequire(path.join(root, 'tests/NativeChannels.Browser.Tests/package.json'));
const { chromium, firefox } = require('playwright');
const published = path.resolve(process.argv[2] ?? path.join(root, 'samples/Avalonia/Currency/Currency.Browser/bin/Release/net10.0-browser/publish/wwwroot'));
const output = path.resolve(process.argv[3] ?? path.join(root, 'artifacts/shared-hosting-browser'));
const inputs = JSON.parse(await readFile(path.join(output, 'inputs.json'), 'utf8'));
const hash = data => createHash('sha256').update(data).digest('hex');
async function files(directory) {
    const result = [];
    for (const entry of await readdir(directory, { withFileTypes: true })) {
        const name = path.join(directory, entry.name);
        if (entry.isDirectory()) result.push(...await files(name));
        else if (entry.isFile()) result.push(path.relative(published, name).split(path.sep).join('/'));
        else throw new Error('Unexpected symlink or special published file.');
    }
    return result;
}
assert.deepEqual((await files(published)).sort(), Object.keys(inputs.publishedFiles).sort(), 'Published file inventory changed');
for (const [name, expected] of Object.entries(inputs.publishedFiles))
    assert.equal(hash(await readFile(path.join(published, name))), expected, `Published bytes changed: ${name}`);

const { server, url } = await serveBrowser(published);
try {
    for (const [name, engine] of [['chromium', chromium], ['firefox', firefox]]) {
        const folder = path.join(output, name);
        await mkdir(folder, { recursive: true });
        for (const file of ['verification.json', 'failure.json']) await rm(path.join(folder, file), { force: true });
        const browser = await engine.launch({ headless: true,
            ...(name === 'chromium' && process.env.BROWSER_CHROMIUM_CHANNEL ? { channel: process.env.BROWSER_CHROMIUM_CHANNEL } : {}) });
        const consoleLines = [], errors = [], runs = [], requests = new Set();
        let success = false;
        try {
            const context = await browser.newContext({ viewport: { width: 1000, height: 900 }, locale: 'en-US', colorScheme: 'light' });
            const page = await context.newPage();
            page.on('console', message => {
                consoleLines.push(`${message.type()}: ${message.text()}`);
                if (message.type() === 'error') errors.push(message.text());
            });
            page.on('pageerror', error => errors.push(error.message));
            page.on('requestfailed', request => errors.push(`${request.url()}: ${request.failure()?.errorText}`));
            page.on('response', response => {
                if (response.status() >= 400) errors.push(`${response.status()}: ${response.url()}`);
                requests.add(new URL(response.url()).pathname);
            });
            for (let iteration = 0; iteration < 2; iteration++) {
                if (iteration === 0) await page.goto(url); else await page.reload();
                await page.waitForFunction(() => globalThis.currencyApp && currencyApp.GetVerificationResult() !== 'pending', undefined, { timeout: 90_000 });
                const result = await page.evaluate(() => currencyApp.GetVerificationResult());
                assert.match(result, /^PASS browser: USD=.+, EUR=€, GBP=.+, JPY=.+; normalization, errors, concurrency, cancellation$/u);
                const transport = await page.evaluate(() => Promise.race([
                    currencyApp.VerifyTransportAsync(),
                    new Promise((_, reject) => setTimeout(() => reject(new Error('Transport verification timed out')), 60_000))
                ]));
                assert.match(transport, /^PASS browser transport:/u);
                await page.locator('canvas').first().waitFor({ state: 'visible' });
                // Avalonia paints controls on a canvas. Coordinates match the fixed 1000x900
                // viewport; read the rendered control text through the sample's inspection hook.
                await page.mouse.click(532, 314); // GBP shortcut
                await page.waitForFunction(() => currencyApp.GetDisplayedText().includes('£'));
                await page.mouse.click(450, 188); // Currency code textbox
                await page.keyboard.press('ControlOrMeta+A');
                await page.keyboard.type('ZZZ');
                await page.keyboard.press('Enter');
                await page.waitForFunction(() => currencyApp.GetDisplayedText().includes('Unknown ISO currency code.'));
                await page.mouse.click(468, 314); // EUR shortcut, recovery after an invalid code
                await page.waitForFunction(() => currencyApp.GetDisplayedText().includes('€'));
                if (iteration === 0) await page.screenshot({ path: path.join(folder, 'screenshot.png') });
                await page.evaluate(() => currencyApp.CloseAsync());
                await page.evaluate(() => currencyApp.CloseAsync());
                runs.push({ iteration, result, transport, ui: 'GBP shortcut, ZZZ keyboard submission, EUR recovery' });
            }
            assert.equal(consoleLines.filter(line => line === 'log: CURRENCY_AVALONIA_CLOSED').length, 2, 'One orderly close per loaded app');
            assert.deepEqual(errors, [], 'Browser console, network or runtime error');
            for (const name of Object.keys(inputs.packagedModules))
                assert.ok(requests.has('/currency/' + name), `Packaged module not requested: ${name}`);
            const record = { success: true, browser: name, browserVersion: browser.version(),
                testOS: `${os.platform()} ${os.release()} ${os.arch()}`, locale: 'en-US', basePath: '/currency/',
                sourceCommit: inputs.sourceCommit, sourceTreeSha256: inputs.sourceTreeSha256,
                inputManifestSha256: hash(await readFile(path.join(output, 'inputs.json'))),
                workingTreeDirty: inputs.workingTreeDirty, runs, requestedModules: [...requests].filter(name => name.endsWith('.mjs')).sort() };
            await writeFile(path.join(folder, 'verification.json'), JSON.stringify(record, null, 2) + '\n');
            success = true;
            console.log(`PASS ${name} ${browser.version()}: Currency, managed/JS transport, reload and orderly disposal`);
            await context.close();
        } finally {
            await writeFile(path.join(folder, 'console.log'), consoleLines.join('\n') + '\n');
            if (!success) await writeFile(path.join(folder, 'failure.json'), JSON.stringify({ errors, runs }, null, 2) + '\n');
            await browser.close();
        }
    }
} finally { await new Promise(resolve => server.close(resolve)); }
