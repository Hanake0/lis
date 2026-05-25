import * as vscode from 'vscode';

import { ChatPanelManager } from './chatPanel';
import { DEFAULT_SERVER_URL, readConfigFrom, type LisConfig } from './config';
import { Logger } from './logger';
import { LisStatusBar } from './statusBar';
import { SidebarWebviewProvider } from './sidebarWebviewProvider';

const FIRST_RUN_KEY = 'lis.firstRunNotificationDismissed';

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

	sidebar   = new SidebarWebviewProvider(context.extensionUri, getConfig);
	panels    = new ChatPanelManager(context, getConfig);
	statusBar = new LisStatusBar();

	// All transport state now lives in the webview's LisRpcClient — the SSE channel
	// there tracks the real connect/disconnect state. The host-side status bar shows
	// configured/not-configured as a proxy; live status updates would need a new
	// extension-host signal from the webview if we ever surface them in the chrome.
	statusBar.update(cfg.isConfigured ? 'connected' : 'disconnected', cfg.baseOrigin);

	sidebar.setOpenSessionHandler((chatId, title) => {
		panels?.openOrFocus(chatId, title);
	});

	context.subscriptions.push(
		channel,
		statusBar,
		vscode.window.registerWebviewViewProvider('lis.sidebar', sidebar),

		vscode.commands.registerCommand('lis.showOutput', () => logger?.show()),
		vscode.commands.registerCommand('lis.configure',  () =>
			vscode.commands.executeCommand('workbench.action.openSettings', '@ext:lis.lis-vscode')),

		vscode.commands.registerCommand('lis.newChat', async () => {
			const chatId = `web:${crypto.randomUUID()}`;
			const created = await createChatViaRest(getConfig(), chatId, 'New Chat', logger);
			if (!created) {
				// Don't open a panel for a non-existent chat — the create attempt failed.
				void vscode.window.showWarningMessage('Lis: could not create a new chat (check the server URL/token in settings).');
				return;
			}
			panels?.openOrFocus(created.chatId, created.name ?? 'New Chat');
		}),

		vscode.commands.registerCommand('lis.openChat', (chatId: string, title?: string) => {
			if (!chatId) return;
			panels?.openOrFocus(chatId, title);
		}),

		vscode.workspace.onDidChangeConfiguration((e) => {
			if (!e.affectsConfiguration('lis')) return;
			cfg = readConfigFrom(vscode.workspace.getConfiguration('lis'));
			logger?.setVerbose(cfg.verbose);
			statusBar?.update(cfg.isConfigured ? 'connected' : 'disconnected', cfg.baseOrigin);
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
	// with a direct path to settings. Most installs need the token to connect successfully.
	if (!cfg.isConfigured && !context.globalState.get<boolean>(FIRST_RUN_KEY)) {
		void promptFirstRun(context, cfg.baseOrigin || DEFAULT_SERVER_URL);
	}
}

export function deactivate(): void {
	statusBar?.dispose();
	logger?.dispose();
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

interface CreatedChatRest {
	chatId: string;
	name:   string | null;
}

/**
 * Extension-host-side `POST /api/chats` for the `lis.newChat` command. The
 * webview normally does this itself via LisRpcClient, but commands fire from
 * the host directly so we keep a thin helper here.
 */
async function createChatViaRest(
	cfg:     LisConfig,
	chatId:  string,
	name:    string,
	logger?: Logger | null,
): Promise<CreatedChatRest | null> {
	if (!cfg.baseOrigin) {
		logger?.warn('lis.newChat: no baseOrigin configured');
		return null;
	}
	const url = `${cfg.baseOrigin}/api/chats`;
	try {
		const res = await fetch(url, {
			method:  'POST',
			headers: {
				'Content-Type':  'application/json',
				...(cfg.token ? { Authorization: `Bearer ${cfg.token}` } : {}),
			},
			body: JSON.stringify({ chat_id: chatId, name }),
		});
		if (!res.ok) {
			const text = await res.text().catch(() => '');
			logger?.warn(`lis.newChat: POST /api/chats → ${res.status} ${text}`);
			return null;
		}
		const body = await res.json() as { chat_id?: string; name?: string | null };
		if (!body.chat_id) return null;
		return { chatId: body.chat_id, name: body.name ?? null };
	} catch (err) {
		logger?.error('lis.newChat: REST call failed', err);
		return null;
	}
}
