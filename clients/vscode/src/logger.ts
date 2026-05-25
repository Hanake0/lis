import * as vscode from 'vscode';

type Level = 'info' | 'warn' | 'error';

/**
 * Envelope the webview sends when it wants to write to the Lis output channel.
 * The host receives this through every webview's onDidReceiveMessage handler
 * and forwards to its Logger.
 */
export interface WebviewLogMessage {
	type:    'lis.log';
	level:   Level;
	kind?:   string;
	message: string;
	data?:   unknown;
}

/**
 * Thin wrapper over vscode.OutputChannel. Frames are gated behind a verbose
 * toggle so noisy protocol traffic doesn't drown out signal during normal use.
 */
export class Logger {
	constructor(
		private readonly channel: vscode.OutputChannel,
		private verbose: boolean,
	) {}

	setVerbose(verbose: boolean): void {
		if (this.verbose === verbose) return;
		this.verbose = verbose;
		this.info(`verbose logging ${verbose ? 'enabled' : 'disabled'}`);
	}

	isVerbose(): boolean { return this.verbose; }

	info (message: string): void { this.write('info',  message); }
	warn (message: string, err?: unknown): void { this.write('warn',  message + formatErr(err)); }
	error(message: string, err?: unknown): void { this.write('error', message + formatErr(err)); }

	frame(direction: 'send' | 'recv', frame: unknown): void {
		if (!this.verbose) return;
		this.write('info', `${direction === 'send' ? '→' : '←'} ${safeStringify(frame)}`);
	}

	show(preserveFocus = true): void {
		this.channel.show(preserveFocus);
	}

	dispose(): void {
		this.channel.dispose();
	}

	private write(level: Level, message: string): void {
		const ts = new Date().toISOString();
		this.channel.appendLine(`${ts} [${level}] ${message}`);
	}
}

/**
 * Forward a `lis.log` envelope from the webview to the host's OutputChannel via the
 * Logger. `event(kind, …)` traces are silently dropped when verbose is off so the
 * webview can fire them unconditionally — the gating lives in one place (host).
 */
export function relayWebviewLog(logger: Logger | null, msg: unknown): void {
	if (!logger || !isWebviewLogMessage(msg)) return;
	const tag = msg.kind ? `[webview:${msg.kind}]` : '[webview]';
	const body = msg.data === undefined ? msg.message : `${msg.message} ${safeStringify(msg.data)}`;
	if (msg.kind && !logger.isVerbose()) return; // gated trace — drop when verbose off
	if (msg.level === 'warn')  return logger.warn (`${tag} ${body}`);
	if (msg.level === 'error') return logger.error(`${tag} ${body}`);
	logger.info(`${tag} ${body}`);
}

function isWebviewLogMessage(v: unknown): v is WebviewLogMessage {
	if (!v || typeof v !== 'object') return false;
	const m = v as { type?: unknown; level?: unknown; message?: unknown };
	return m.type === 'lis.log'
		&& (m.level === 'info' || m.level === 'warn' || m.level === 'error')
		&& typeof m.message === 'string';
}

function formatErr(err: unknown): string {
	if (err === undefined) return '';
	if (err instanceof Error) return ` — ${err.message}`;
	return ` — ${safeStringify(err)}`;
}

function safeStringify(value: unknown): string {
	try { return JSON.stringify(value); }
	catch { return String(value); }
}
