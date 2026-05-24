import * as vscode from 'vscode';

import { LisClient } from './lisClient';
import { Logger } from './logger';
import { SidebarWebviewProvider } from './sidebarWebviewProvider';
import { ChatPanelManager } from './chatPanel';

let client:   LisClient | null            = null;
let sidebar:  SidebarWebviewProvider | null = null;
let panels:   ChatPanelManager | null     = null;
let logger:   Logger | null               = null;

export function activate(context: vscode.ExtensionContext): void {
	const cfg = readConfig();

	const channel = vscode.window.createOutputChannel('Lis');
	logger   = new Logger(channel, cfg.verbose);
	logger.info('extension activated');

	client   = new LisClient(cfg.url, cfg.token, logger);
	sidebar  = new SidebarWebviewProvider(context.extensionUri, client);
	panels   = new ChatPanelManager(context, client);

	sidebar.setOpenSessionHandler((chatId, title) => {
		panels?.openOrFocus(chatId, title);
	});

	context.subscriptions.push(
		channel,
		vscode.window.registerWebviewViewProvider('lis.sidebar', sidebar),

		vscode.commands.registerCommand('lis.connect',    () => client?.connect()),
		vscode.commands.registerCommand('lis.disconnect', () => client?.disconnect()),
		vscode.commands.registerCommand('lis.showOutput', () => logger?.show()),

		vscode.commands.registerCommand('lis.newChat', () => {
			const chatId = `web:${crypto.randomUUID()}`;
			client?.send({ type: 'create_session', chat_id: chatId, name: 'New Chat' });
			panels?.openOrFocus(chatId, 'New Chat');
		}),

		vscode.commands.registerCommand('lis.openChat', (chatId: string, title?: string) => {
			if (!chatId) return;
			panels?.openOrFocus(chatId, title);
		}),

		vscode.workspace.onDidChangeConfiguration(e => {
			if (!e.affectsConfiguration('lis')) return;
			const next = readConfig();
			logger?.setVerbose(next.verbose);
			client?.updateConfig(next.url, next.token);
		}),
	);

	client.connect();
}

export function deactivate(): void {
	client?.disconnect();
	logger?.dispose();
	client   = null;
	sessions = null;
	panels   = null;
	logger   = null;
}

function readConfig(): { url: string; token: string; verbose: boolean } {
	const cfg     = vscode.workspace.getConfiguration('lis');
	const url     = cfg.get<string>('serverUrl', 'ws://localhost:3010/web/ws');
	const token   = cfg.get<string>('authToken', '');
	const verbose = cfg.get<boolean>('verboseLogging', false);
	return { url, token, verbose };
}
