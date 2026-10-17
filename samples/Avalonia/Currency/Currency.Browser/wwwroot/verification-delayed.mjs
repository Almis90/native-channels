import { register as registerChecks } from './verification.mjs';
export async function register(context) {
    await new Promise(resolve => setTimeout(resolve, 50));
    return registerChecks(context);
}
