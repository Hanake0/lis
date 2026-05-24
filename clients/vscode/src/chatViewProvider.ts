import * as vscode from 'vscode';

import { LisClient, ServerFrame, ConnectionState } from './lisClient';

interface WebviewMessage {
	type:    'ready' | 'send' | 'requestHistory' | 'newChat';
	chatId?: string;
	text?:   string;
}

export class ChatViewProvider implements vscode.WebviewViewProvider {
	public static readonly viewType = 'lis.chat';

	private view:     vscode.WebviewView | null  = null;
	private chats:    string[]                   = [];
	private current:  string | null              = null;

	constructor(
		private readonly context: vscode.ExtensionContext,
		private readonly client:  LisClient,
	) {
		client.onFrame(frame => this.onServerFrame(frame));
		client.onState(state => this.postState(state));
	}

	resolveWebviewView(view: vscode.WebviewView): void {
		this.view = view;

		view.webview.options = {
			enableScripts: true,
			localResourceRoots: [vscode.Uri.joinPath(this.context.extensionUri, 'media')],
		};

		view.webview.html = this.buildHtml(view.webview);

		view.webview.onDidReceiveMessage((msg: WebviewMessage) => this.onWebviewMessage(msg));

		view.onDidDispose(() => { this.view = null; });
	}

	// ── Public actions (commands) ──────────────────────────────────

	newChat(): void {
		const id = `web:${crypto.randomUUID()}`;
		this.chats.push(id);
		this.setCurrent(id);
	}

	// ── Webview → extension ────────────────────────────────────────

	private onWebviewMessage(msg: WebviewMessage): void {
		switch (msg.type) {
			case 'ready':
				this.postState(this.client.currentState);
				this.client.send({ type: 'list_chats' });
				if (this.current) this.postCurrent(this.current);
				break;

			case 'send':
				if (!msg.chatId || !msg.text) return;
				this.client.send({ type: 'message', chat_id: msg.chatId, text: msg.text, dispatch: 'immediate' });
				break;

			case 'requestHistory':
				if (!msg.chatId) return;
				this.client.send({ type: 'history', chat_id: msg.chatId });
				break;

			case 'newChat':
				this.newChat();
				break;
		}
	}

	// ── Server → webview ───────────────────────────────────────────

	private onServerFrame(frame: ServerFrame): void {
		// Track chats we hear about so the selector stays in sync.
		if ((frame.type === 'message' || frame.type === 'chat_history') && !this.chats.includes(frame.chat_id))
			this.chats.push(frame.chat_id);

		if (frame.type === 'chat_list') {
			for (const c of frame.chats) if (!this.chats.includes(c.chat_id)) this.chats.push(c.chat_id);
			if (!this.current && this.chats.length > 0) this.setCurrent(this.chats[0]);
		}

		this.post({ type: 'frame', frame });
	}

	private setCurrent(chatId: string): void {
		this.current = chatId;
		this.postCurrent(chatId);
	}

	private postCurrent(chatId: string): void {
		this.post({ type: 'currentChat', chatId });
	}

	private postState(state: ConnectionState): void {
		this.post({ type: 'state', state });
	}

	private post(msg: unknown): void {
		this.view?.webview.postMessage(msg);
	}

	// ── HTML template ──────────────────────────────────────────────

	private buildHtml(webview: vscode.Webview): string {
		const mediaRoot = vscode.Uri.joinPath(this.context.extensionUri, 'media');
		const cssUri = webview.asWebviewUri(vscode.Uri.joinPath(mediaRoot, 'chat.css'));
		const jsUri  = webview.asWebviewUri(vscode.Uri.joinPath(mediaRoot, 'chat.js'));
		const nonce  = generateNonce();

		// chat.html ships with literal `${nonce}`, `${cspSource}`, `${cssUri}`, `${jsUri}`
		// placeholders so we don't depend on a templating engine.
		const template = readBundledHtml();
		return template
			.replace(/\$\{nonce\}/g,     nonce)
			.replace(/\$\{cspSource\}/g, webview.cspSource)
			.replace(/\$\{cssUri\}/g,    cssUri.toString())
			.replace(/\$\{jsUri\}/g,     jsUri.toString());
	}
}

// chat.html is bundled as a string literal at build time via esbuild's loader.
// At runtime we read it through a require() of the file as text. esbuild's
// `loader: { '.html': 'text' }` would inline it; for simplicity we ship the
// HTML separately and read it from disk via the extension URI in production.
// For this first cut, we inline a minimal template here so the extension is
// self-contained without extra esbuild loaders.
function readBundledHtml(): string {
	return BUNDLED_HTML;
}

const BUNDLED_HTML = `<!DOCTYPE html>
<html lang="en">
<head>
	<meta charset="UTF-8">
	<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src \${cspSource} 'unsafe-inline'; script-src 'nonce-\${nonce}';">
	<link rel="stylesheet" href="\${cssUri}">
	<title>Lis</title>
</head>
<body>
	<div class="status" id="status">connecting…</div>

	<div class="chat-bar">
		<select id="chat-select" aria-label="Chat"></select>
		<button id="new-chat" title="New chat">+</button>
	</div>

	<div class="messages" id="messages"></div>

	<div class="typing" id="typing" hidden>
		<span></span><span></span><span></span>
	</div>

	<form class="composer" id="composer">
		<textarea id="input" rows="1" placeholder="Message Lis (Enter to send, Shift+Enter for newline)" autofocus></textarea>
		<button type="submit" id="send" aria-label="Send">Send</button>
	</form>

	<script nonce="\${nonce}" src="\${jsUri}"></script>
</body>
</html>`;

function generateNonce(): string {
	const bytes = new Uint8Array(16);
	crypto.getRandomValues(bytes);
	return Array.from(bytes, b => b.toString(16).padStart(2, '0')).join('');
}
