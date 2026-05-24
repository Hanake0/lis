import * as vscode from 'vscode';

import type { LisConfig } from './config';
import type { LisClient } from './lisClient';
import type { ConnectionState, ServerFrame } from './types';
import { generateNonce } from './util';

interface ImageAttachment {
	filename:  string;
	mime_type: string;
	data_url:  string;
}

interface WebviewInbound {
	type:           'ready' | 'send' | 'send_frame' | 'create_session' | 'delete_session' | 'rename_session' | 'archive_session' | 'lis.ready';
	text?:          string;
	model?:         string;
	effort?:        string;
	tool_profile?:  string;
	thinking_mode?: string;
	attachments?:   ImageAttachment[];
	frame?:         Record<string, unknown>;
	chat_id?:       string;
	name?:          string;
}

interface ChatPanel {
	readonly chatId: string;
	title:    string;
	panel:    vscode.WebviewPanel;
	ready:    boolean;
}

/**
 * Owns one editor WebviewPanel per chat_id. The webview talks REST + SSE directly
 * to the Lis backend; the extension host only ships config (lis.init /
 * lis.config_update) and legacy WS frames through postMessage.
 */
export class ChatPanelManager {
	private static readonly viewType = 'lis.chatPanel';
	private readonly panels = new Map<string, ChatPanel>();

	constructor(
		private readonly context:   vscode.ExtensionContext,
		private readonly client:    LisClient,
		private          getConfig: () => LisConfig,
	) {
		client.onFrame(frame => this.onFrame(frame));
		client.onState(state => this.broadcastState(state));
	}

	/** Push the current config to every open panel — called when settings change. */
	notifyConfigChange(): void {
		const cfg = this.getConfig();
		const msg = { type: 'lis.config_update', baseOrigin: cfg.baseOrigin, token: cfg.token };
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

		const entry: ChatPanel = { chatId, title: resolvedTitle, panel, ready: false };
		this.panels.set(chatId, entry);

		panel.webview.onDidReceiveMessage((msg: WebviewInbound) => this.onWebviewMessage(entry, msg));
		panel.onDidDispose(() => { this.panels.delete(chatId); });
	}

	private onWebviewMessage(entry: ChatPanel, msg: WebviewInbound & { type: string }): void {
		switch (msg.type) {
			case 'lis.ready': {
				// Webview booted; send the current REST/SSE config.
				const cfg = this.getConfig();
				this.post(entry, { type: 'lis.init', baseOrigin: cfg.baseOrigin, token: cfg.token });
				return;
			}

			case 'toggle_sidebar':
				void vscode.commands.executeCommand('workbench.view.extension.lis-sidebar');
				return;
			case 'ready':
				entry.ready = true;
				this.post(entry, { type: 'init', chatId: entry.chatId });
				this.post(entry, { type: 'state', state: this.client.currentState });
				this.client.send({ type: 'history',     chat_id: entry.chatId });
				this.client.send({ type: 'get_agent',   chat_id: entry.chatId });
				this.client.send({ type: 'list_agents' });
				this.client.send({ type: 'list_chats' });
				break;

			case 'send':
				if (!msg.text && !msg.attachments?.length) return;
				this.client.send({
					type: 'message', chat_id: entry.chatId, text: msg.text ?? '', dispatch: 'immediate',
					model: msg.model, effort: msg.effort, tool_profile: msg.tool_profile,
					thinking_mode: msg.thinking_mode,
					attachments: msg.attachments,
				});
				break;

			case 'send_frame':
				if (msg.frame) this.client.send(msg.frame as never);
				break;

			case 'create_session':
				if (msg.chat_id) this.client.send({ type: 'create_session', chat_id: msg.chat_id, name: msg.name });
				break;

			case 'delete_session':
				if (msg.chat_id) this.client.send({ type: 'delete_session', chat_id: msg.chat_id });
				break;

			case 'rename_session':
				if (msg.chat_id && msg.name) this.client.send({ type: 'rename_session', chat_id: msg.chat_id, name: msg.name });
				break;

			case 'archive_session':
				if (msg.chat_id) this.client.send({ type: 'archive_session', chat_id: msg.chat_id });
				break;
		}
	}

	private onFrame(frame: ServerFrame): void {
		if (frame.type === 'connected') return;

		// Global frames — broadcast to all panels
		if (frame.type === 'agent_list' || frame.type === 'chat_list'
			|| frame.type === 'chat_created' || frame.type === 'chat_deleted'
			|| frame.type === 'chat_renamed' || frame.type === 'chat_archived') {
			for (const entry of this.panels.values()) this.post(entry, { type: 'frame', frame });
			return;
		}

		const chatId = (frame as { chat_id?: string }).chat_id;
		if (!chatId) return;

		const entry = this.panels.get(chatId);
		if (entry) this.post(entry, { type: 'frame', frame });
	}

	private broadcastState(state: ConnectionState): void {
		for (const entry of this.panels.values()) this.post(entry, { type: 'state', state });
	}

	private post(entry: ChatPanel, msg: unknown): void {
		entry.panel.webview.postMessage(msg);
	}

	private buildHtml(webview: vscode.Webview): string {
		const distRoot   = vscode.Uri.joinPath(this.context.extensionUri, 'dist');
		const cssUri     = webview.asWebviewUri(vscode.Uri.joinPath(distRoot, 'chat.css'));
		const jsUri      = webview.asWebviewUri(vscode.Uri.joinPath(distRoot, 'chat.js'));
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
