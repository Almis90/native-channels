// Deliberately fails registration to exercise asynchronous initialization cleanup.
export function register() { throw new Error('Expected verification initialization failure.'); }
