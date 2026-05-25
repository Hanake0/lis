import * as vscode from 'vscode';

import type { LisConfig } from './config';
import { installHostFsHandler } from './hostFs';
import { type Logger, relayWebviewLog } from './logger';
import { buildWorkspaceMessage, openExternalPath, safeMtimeMs } from './sidebarWebviewProvider';
import { generateNonce } from './util';

interface WebviewInbound {
	type:           'ready' | 'lis.ready' | 'host_open_external' | 'toggle_sidebar';
	path?:          string;
}

interface ChatPanel {
	readonly chatId: string;
	title:    string;
	panel:    vscode.WebviewPanel;
}

/**
 * Owns one editor WebviewPanel per chat_id. The webview talks REST + SSE
 * directly to the Lis backend — the extension host's only responsibility is
 * to provide config (lis.init / lis.config_update), workspace info, and
 * route the inbound `init { chatId }` envelope so the panel knows which chat
 * to bind to. No WebSocket, no frame proxying.
 */
export class ChatPanelManager {
	private static readonly viewType = 'lis.chatPanel';
	private readonly panels = new Map<string, ChatPanel>();

	constructor(
		private readonly context:   vscode.ExtensionContext,
		private          getConfig: () => LisConfig,
		private readonly logger:    Logger | null = null,
	) {}

	/** Push the current config to every open panel — called when settings change. */
	notifyConfigChange(): void {
		const cfg = this.getConfig();
		const msg = { type: 'lis.config_update', baseOrigin: cfg.baseOrigin, token: cfg.token, verbose: cfg.verbose };
		for (const entry of this.panels.values()) entry.panel.webview.postMessage(msg);
		this.logger?.info(`chatPanel: pushed config to ${this.panels.size} panel(s) (origin=${cfg.baseOrigin || '∅'}, token=${cfg.token ? 'set' : 'missing'}, verbose=${cfg.verbose})`);
	}

	/** Push the current VS Code workspace root + home directory to every open panel. */
	notifyWorkspaceChange(): void {
		const msg = buildWorkspaceMessage();
		for (const entry of this.panels.values()) entry.panel.webview.postMessage(msg);
	}

	openOrFocus(chatId: string, title?: string): void {
		const existing = this.panels.get(chatId);
		if (existing) {
			if (title && title !== existing.title) {
				existing.title       = title;
				existing.panel.title = title;
			}
			existing.panel.reveal(undefined, true);
			return;
		}

		const resolvedTitle = title && title.length > 0 ? title : 'Chat';
		const panel = vscode.window.createWebviewPanel(
			ChatPanelManager.viewType,
			resolvedTitle,
			{ viewColumn: vscode.ViewColumn.Active, preserveFocus: false },
			{
				enableScripts:           true,
				retainContextWhenHidden: true,
				localResourceRoots: [
					vscode.Uri.joinPath(this.context.extensionUri, 'media'),
					vscode.Uri.joinPath(this.context.extensionUri, 'dist'),
				],
			},
		);
		panel.iconPath     = vscode.Uri.joinPath(this.context.extensionUri, 'media', 'lis-noturna.svg');
		panel.webview.html = this.buildHtml(panel.webview);

		const entry: ChatPanel = { chatId, title: resolvedTitle, panel };
		this.panels.set(chatId, entry);

		// installHostFsHandler intercepts `host_fs` messages and forwards everything else
		// to our existing onWebviewMessage. Same flow as SidebarWebviewProvider — the
		// chat panel needs the FS bridge for SidebarFilesTree / FilesView / etc.
		installHostFsHandler(panel.webview, (msg) => {
			if (msg?.type === 'lis.log') { relayWebviewLog(this.logger, msg); return; }
			this.onWebviewMessage(entry, msg as WebviewInbound & { type: string });
		}, this.logger);
		panel.onDidDispose(() => { this.panels.delete(chatId); this.logger?.info(`chatPanel: disposed chat=${chatId}`); });
	}

	private onWebviewMessage(entry: ChatPanel, msg: WebviewInbound & { type: string }): void {
		switch (msg.type) {
			case 'lis.ready':
			case 'ready': {
				// First message from the React mount — push config + workspace, then
				// tell the panel which chat it owns so the bridge can subscribe.
				const cfg = this.getConfig();
				this.logger?.info(`chatPanel ${entry.chatId}: handshake (${msg.type}) — pushing init (origin=${cfg.baseOrigin || '∅'}, token=${cfg.token ? 'set' : 'missing'}, verbose=${cfg.verbose})`);
				this.post(entry, { type: 'lis.init', baseOrigin: cfg.baseOrigin, token: cfg.token, verbose: cfg.verbose });
				this.post(entry, buildWorkspaceMessage());
				this.post(entry, { type: 'init', chatId: entry.chatId });
				return;
			}

			case 'host_open_external':
				if (typeof msg.path === 'string' && msg.path.length > 0) void openExternalPath(msg.path);
				return;

			case 'toggle_sidebar':
				void vscode.commands.executeCommand('workbench.view.extension.lis-sidebar');
				return;
		}
	}

	private post(entry: ChatPanel, msg: unknown): void {
		entry.panel.webview.postMessage(msg);
	}

	private buildHtml(webview: vscode.Webview): string {
		const distRoot   = vscode.Uri.joinPath(this.context.extensionUri, 'dist');
		const cssDisk    = vscode.Uri.joinPath(distRoot, 'chat.css');
		const jsDisk     = vscode.Uri.joinPath(distRoot, 'chat.js');
		// See sidebarWebviewProvider.safeMtimeMs — mtime cache-bust so Chromium /
		// VS Code Web don't serve a stale bundle after esbuild rebuilds it.
		const cssUri     = `${webview.asWebviewUri(cssDisk)}?v=${safeMtimeMs(cssDisk.fsPath)}`;
		const jsUri      = `${webview.asWebviewUri(jsDisk)}?v=${safeMtimeMs(jsDisk.fsPath)}`;
		const nonce      = generateNonce();
		const cspSource  = webview.cspSource;
		const apiOrigin  = this.getConfig().baseOrigin;
		// CSP connect-src includes the user-configured backend so fetch() + EventSource
		// can reach /api/* directly. If empty, only the extension's own resources are allowed.
		const connectSrc = apiOrigin ? `${cspSource} ${apiOrigin}` : cspSource;

		return `<!DOCTYPE html>
<html lang="en">
<head>
	<meta charset="UTF-8">
	<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src ${cspSource} 'unsafe-inline'; script-src 'nonce-${nonce}'; font-src data: ${cspSource}; img-src data: ${cspSource} https:; connect-src ${connectSrc};">
	<link rel="stylesheet" href="${cssUri}">
	<title>Lis Chat</title>
</head>
<body>
	<div id="app"></div>
	<script nonce="${nonce}" src="${jsUri}"></script>
</body>
</html>`;
	}
}
