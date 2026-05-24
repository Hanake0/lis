// Typed wrapper for the VS Code webview API.
//
// `acquireVsCodeApi()` is injected by the VS Code webview host at runtime.
// In test or non-VS-Code environments (bun, node) it's absent — we hand back
// a quiet stub so the module graph still resolves. The stub stores state in
// memory which is enough for unit tests that exercise other modules.

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

export const vscode = resolveVsCodeApi();
