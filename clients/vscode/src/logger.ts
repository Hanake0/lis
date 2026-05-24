import * as vscode from 'vscode';

type Level = 'info' | 'warn' | 'error';

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

function formatErr(err: unknown): string {
	if (err === undefined) return '';
	if (err instanceof Error) return ` — ${err.message}`;
	return ` — ${safeStringify(err)}`;
}

function safeStringify(value: unknown): string {
	try { return JSON.stringify(value); }
	catch { return String(value); }
}
