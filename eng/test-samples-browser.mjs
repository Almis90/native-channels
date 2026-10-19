// Local real-browser execution; the button supplies the audio user gesture.
import assert from 'node:assert/strict';
import { writeFile } from 'node:fs/promises';
import { appendFileSync } from 'node:fs';
import path from 'node:path';
import { createRequire } from 'node:module';
import { serveBrowser } from './serve-browser.mjs';

const require = createRequire(new URL('../tests/NativeChannels.Browser.Tests/package.json', import.meta.url));
const { chromium } = require('playwright');
const [directory, sample, output] = process.argv.slice(2);
const { server, url } = await serveBrowser(path.join(directory, 'wwwroot'));
let browser;
const log = [], errors = [];
const recordError = message => {
    errors.push(message);
    appendFileSync(path.join(output, 'stderr.log'), message + '\n');
};
try {
    browser = await chromium.launch({ headless: true,
        ...(process.env.BROWSER_CHROMIUM_CHANNEL ? { channel: process.env.BROWSER_CHROMIUM_CHANNEL } : {}) });
    const page = await browser.newPage({ viewport: { width: 1100, height: 1000 } });
    page.on('console', event => {
        log.push(event.text());
        appendFileSync(path.join(output, 'stdout.log'), event.text() + '\n');
        if (event.type() === 'error') recordError(event.text());
    });
    page.on('pageerror', error => recordError(error.message));
    page.on('requestfailed', request => recordError(`${request.url()}: ${request.failure()?.errorText}`));
    page.on('response', response => { if (response.status() >= 400) recordError(`${response.status()}: ${response.url()}`); });
    await page.goto(url);
    await page.locator('canvas').first().waitFor({ state: 'visible', timeout: 90000 });
    let result;
    if (sample === 'Currency') {
        await page.waitForFunction(() => globalThis.currencyApp?.GetVerificationResult() !== undefined
            && currencyApp.GetVerificationResult() !== 'pending', undefined, { timeout: 90000 });
        result = await page.evaluate(() => currencyApp.GetVerificationResult());
        assert.match(await page.evaluate(() => currencyApp.VerifyTransportAsync()), /^PASS /);
        await page.evaluate(() => currencyApp.CloseAsync());
        assert.equal(log.filter(line => line === 'CURRENCY_AVALONIA_CLOSED').length, 1);
    } else {
        await page.waitForFunction(() => globalThis.sampleApp, undefined, { timeout: 90000 });
        await page.evaluate(() => {
            const button = document.createElement('button');
            button.id = 'local-verification';
            button.textContent = 'Run local verification';
            Object.assign(button.style, { position: 'fixed', top: '0', right: '0', zIndex: '99999' });
            button.onclick = () => {
                globalThis.localVerification = sampleApp.VerifyAsync();
                // Attach a handler immediately, including before the Playwright evaluation.
                localVerification.catch(() => {});
            };
            document.body.append(button);
        });
        await page.locator('#local-verification').click();
        result = await page.evaluate(() => localVerification);
    }
    assert.match(result, /^PASS /);
    assert.deepEqual(errors, []);
    await page.screenshot({ path: path.join(output, 'screenshot.png') });
    await writeFile(path.join(output, 'result.txt'), result);
} finally {
    await writeFile(path.join(output, 'stdout.log'), log.join('\n'));
    await writeFile(path.join(output, 'stderr.log'), errors.join('\n'));
    await browser?.close();
    await new Promise(resolve => server.close(resolve));
}
