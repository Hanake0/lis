import * as vscode from 'vscode';

import { ChatPanelManager } from './chatPanel';
import { DEFAULT_SERVER_URL, deriveWsUrl, readConfigFrom, type LisConfig } from './config';
import { LisClient } from './lisClient';
import { Logger } from './logger';
import { LisStatusBar } from './statusBar';
import { SidebarWebviewProvider } from './sidebarWebviewProvider';

const FIRST_RUN_KEY = 'lis.firstRunNotificationDismissed';

let client:    LisClient | null = null;
let sidebar:   SidebarWebviewProvider | null = null;
let panels:    ChatPanelManager | null = null;
let logger:    Logger | null = null;
let statusBar: LisStatusBar | null = null;

export function activate(context: vscode.ExtensionContext): void {
	let cfg: LisConfig = readConfigFrom(vscode.workspace.getConfiguration('lis'));
	const getConfig = (): LisConfig => cfg;

	const channel = vscode.window.createOutputChannel('Lis');
	logger = new Logger(channel, cfg.verbose);
	logger.info('extension activated');

	// One-shot migration: rewrite legacy ws://…/web/ws to the canonical base origin
	// so the settings UI reflects the new shape. Fire-and-forget; it can't block activation.
	if (cfg.isLegacyUrl) {
		void vscode.workspace.getConfiguration('lis').update(
			'serverUrl',
			cfg.baseOrigin,
			vscode.ConfigurationTarget.Global,
		).then(() => {
			logger?.info(`migrated lis.serverUrl to base origin: ${cfg.baseOrigin}`);
		});
	}

	const wsUrl = deriveWsUrl(cfg.baseOrigin);
	client    = new LisClient(wsUrl, cfg.token, logger);
	sidebar   = new SidebarWebviewProvider(context.extensionUri, client, getConfig);
	panels    = new ChatPanelManager(context, client, getConfig);
	statusBar = new LisStatusBar();

	statusBar.update(client.currentState, cfg.baseOrigin);
	client.onState((state) => {
		statusBar?.update(state, getConfig().baseOrigin);
	});

	sidebar.setOpenSessionHandler((chatId, title) => {
		panels?.openOrFocus(chatId, title);
	});

	context.subscriptions.push(
		channel,
		statusBar,
		vscode.window.registerWebviewViewProvider('lis.sidebar', sidebar),

		vscode.commands.registerCommand('lis.connect',    () => client?.connect()),
		vscode.commands.registerCommand('lis.disconnect', () => client?.disconnect()),
		vscode.commands.registerCommand('lis.showOutput', () => logger?.show()),
		vscode.commands.registerCommand('lis.configure',  () =>
			vscode.commands.executeCommand('workbench.action.openSettings', '@ext:lis.lis-vscode')),

		vscode.commands.registerCommand('lis.newChat', () => {
			const chatId = `web:${crypto.randomUUID()}`;
			client?.send({ type: 'create_session', chat_id: chatId, name: 'New Chat' });
			panels?.openOrFocus(chatId, 'New Chat');
		}),

		vscode.commands.registerCommand('lis.openChat', (chatId: string, title?: string) => {
			if (!chatId) return;
			panels?.openOrFocus(chatId, title);
		}),

		vscode.workspace.onDidChangeConfiguration((e) => {
			if (!e.affectsConfiguration('lis')) return;
			cfg = readConfigFrom(vscode.workspace.getConfiguration('lis'));
			logger?.setVerbose(cfg.verbose);
			client?.updateConfig(deriveWsUrl(cfg.baseOrigin), cfg.token);
			statusBar?.update(client?.currentState ?? 'disconnected', cfg.baseOrigin);
			// Push the new origin + token to every live webview so its LisRpcClient
			// re-opens the EventSource against the new server without a reload.
			sidebar?.notifyConfigChange();
			panels?.notifyConfigChange();
		}),

		// Mirror VS Code workspace changes into the webview's useDirectoryStore so
		// `currentDirectory` follows the user when they open a different folder.
		vscode.workspace.onDidChangeWorkspaceFolders(() => {
			sidebar?.notifyWorkspaceChange();
			panels?.notifyWorkspaceChange();
		}),
	);

	// First-run UX: if the user has never set a token, surface a one-shot notification
	// with a direct path to settings. Default serverUrl is good enough to skip if
	// they really want to point at localhost without auth, but most installs need
	// the token to connect successfully.
	if (!cfg.isConfigured && !context.globalState.get<boolean>(FIRST_RUN_KEY)) {
		void promptFirstRun(context, cfg.baseOrigin || DEFAULT_SERVER_URL);
	}

	// Connect regardless — if no token is set the user can opt in to "Connect anyway"
	// from the notification, and the WS layer will surface auth failures via the status bar.
	client.connect();
}

export function deactivate(): void {
	client?.disconnect();
	statusBar?.dispose();
	logger?.dispose();
	client    = null;
	sidebar   = null;
	panels    = null;
	statusBar = null;
	logger    = null;
}

async function promptFirstRun(context: vscode.ExtensionContext, serverUrl: string): Promise<void> {
	const choice = await vscode.window.showInformationMessage(
		`Lis is not fully configured (server: ${serverUrl}, token: missing). Set the auth token to connect.`,
		'Open Settings',
		'Dismiss',
	);
	if (choice === 'Open Settings') {
		void vscode.commands.executeCommand('lis.configure');
	}
	// Either choice (or dismissal) marks the prompt seen so we don't nag on every reload.
	await context.globalState.update(FIRST_RUN_KEY, true);
}
