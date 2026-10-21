let finish;
export function register(verify) {
    globalThis.sampleApp = { VerifyAsync: () => new Promise(resolve => { finish = resolve; verify(); }) };
}
export function complete(result) { finish(result); }
