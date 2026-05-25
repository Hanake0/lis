// Typed wrapper for the VS Code webview API.
//
// `acquireVsCodeApi()` is injected by the VS Code webview host at runtime.
// In test or non-VS-Code environments (bun, node) it's absent — we hand back
// a quiet stub so the module graph still resolves. The stub stores state in
// memory which is enough for unit tests that exercise other modules.
//
// Every outbound postMessage is traced via lisLog.event('postMessage.out', …)
// when `lis.verboseLogging` is on. We exclude `lis.log` envelopes to avoid an
// infinite loop (logging the log frames themselves).

import { isLisLogVerbose, lisLog } from './lib/lis/logger';

interface VsCodeApi {
	postMessage(msg: unknown): void;
	getState(): unknown;
	setState(state: unknown): void;
}

declare function acquireVsCodeApi(): VsCodeApi;

function resolveVsCodeApi(): VsCodeApi {
	if (typeof acquireVsCodeApi === 'function') return acquireVsCodeApi();

	let state: unknown = undefined;
	return {
		postMessage: () => { /* test/standalone: drop messages on the floor */ },
		getState:    () => state,
		setState:    (s) => { state = s; },
	};
}

const inner = resolveVsCodeApi();

/**
 * Wrap postMessage so verbose mode prints every outbound envelope. The exclusion
 * for `lis.log` prevents an infinite loop (the log frame would itself be logged).
 */
export const vscode: VsCodeApi = {
	postMessage(msg: unknown): void {
		const type = (msg as { type?: unknown })?.type;
		if (isLisLogVerbose() && type !== 'lis.log') {
			lisLog.event('postMessage.out', { type, msg });
		}
		inner.postMessage(msg);
	},
	getState: ()  => inner.getState(),
	setState: (s) => inner.setState(s),
};

/**
 * Hook installed once at boot — every inbound postMessage from the extension
 * host is traced when verbose. Sits in front of all per-feature listeners.
 */
export function installInboundMessageTracer(target: Window | undefined = typeof window !== 'undefined' ? window : undefined): () => void {
	if (!target) return () => {};
	const handler = (e: MessageEvent) => {
		if (!isLisLogVerbose()) return;
		const type = (e.data as { type?: unknown })?.type;
		lisLog.event('postMessage.in', { type, msg: e.data });
	};
	target.addEventListener('message', handler);
	return () => target.removeEventListener('message', handler);
}
