/**
 * Webview-side logger. The webview can't write to vscode.OutputChannel directly,
 * so every log call postMessages a `lis.log` envelope to the extension host,
 * which appends it to the Lis output channel via the host-side Logger.
 *
 * Three knobs:
 *
 *   - `info / warn / error` are unconditional — they always flow to the host so
 *     real failures stay visible without flipping a setting.
 *   - `event(kind, data)` is gated by the `verbose` flag (sourced from
 *     `lis.verboseLogging`). Use it for high-volume protocol traffic — HTTP
 *     requests, SSE events, postMessage round-trips — where you want trace-level
 *     visibility when debugging but silence by default.
 *   - `setVerbose` is called by LisRpcClient when the host pushes `lis.init` /
 *     `lis.config_update` so toggling the setting at runtime takes effect
 *     without a webview reload.
 */

import { vscode } from '@/vscodeApi';

export type LisLogLevel = 'info' | 'warn' | 'error';

interface LogEnvelope {
	type:    'lis.log';
	level:   LisLogLevel;
	kind?:   string;
	message: string;
	data?:   unknown;
}

let verbose = false;

export function setLisLogVerbose(next: boolean): void {
	verbose = next;
}

export function isLisLogVerbose(): boolean { return verbose; }

function send(level: LisLogLevel, message: string, data: unknown, kind?: string): void {
	const envelope: LogEnvelope = { type: 'lis.log', level, message };
	if (kind         !== undefined) envelope.kind = kind;
	if (data         !== undefined) envelope.data = sanitise(data);
	try { vscode.postMessage(envelope); } catch { /* webview detached — best-effort */ }
}

/**
 * Stringify-friendly snapshot of an unknown value. Trims giant payloads so
 * verbose SSE traces don't make the output channel unreadable, and converts
 * Error instances into plain `{ message, stack }` shapes.
 */
function sanitise(value: unknown): unknown {
	if (value instanceof Error) return { message: value.message, stack: value.stack };
	if (typeof value === 'string' && value.length > 2000) return `${value.slice(0, 2000)}… (+${value.length - 2000} chars)`;
	return value;
}

export const lisLog = {
	info (message: string, data?: unknown): void { send('info',  message, data); },
	warn (message: string, data?: unknown): void { send('warn',  message, data); },
	error(message: string, data?: unknown): void { send('error', message, data); },

	/** Verbose-gated trace event. `kind` groups related traces (e.g. `http.request`, `sse.event`). */
	event(kind: string, data?: unknown): void {
		if (!verbose) return;
		send('info', kind, data, kind);
	},
};
