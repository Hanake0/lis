import * as vscode from 'vscode';

import { LisClient } from './lisClient';
import { ChatViewProvider } from './chatViewProvider';

let client:   LisClient | null         = null;
let provider: ChatViewProvider | null  = null;

export function activate(context: vscode.ExtensionContext): void {
	const { url, token } = readConfig();
	client   = new LisClient(url, token);
	provider = new ChatViewProvider(context, client);

	context.subscriptions.push(
		vscode.window.registerWebviewViewProvider(ChatViewProvider.viewType, provider),

		vscode.commands.registerCommand('lis.connect',    () => client?.connect()),
		vscode.commands.registerCommand('lis.disconnect', () => client?.disconnect()),
		vscode.commands.registerCommand('lis.newChat',    () => provider?.newChat()),

		vscode.workspace.onDidChangeConfiguration(e => {
			if (!e.affectsConfiguration('lis')) return;
			const next = readConfig();
			client?.updateConfig(next.url, next.token);
		}),
	);

	client.connect();
}

export function deactivate(): void {
	client?.disconnect();
	client   = null;
	provider = null;
}

function readConfig(): { url: string; token: string } {
	const cfg   = vscode.workspace.getConfiguration('lis');
	const url   = cfg.get<string>('serverUrl', 'ws://localhost:3010/web/ws');
	const token = cfg.get<string>('authToken', '');
	return { url, token };
}
