// Webview entry point — one chat per panel.
// The host announces our chat_id via the 'init' message.

import { vscode } from './vscodeApi';
import { handleHostMessage, getChatId } from './dispatcher';
import { showTyping } from './renderer';

const form  = document.getElementById('composer') as HTMLFormElement;
const input = document.getElementById('input') as HTMLTextAreaElement;

// ── UI wiring ──────────────────────────────────────────────────

form.addEventListener('submit', e => {
	e.preventDefault();
	send();
});

input.addEventListener('keydown', e => {
	if (e.key === 'Enter' && !e.shiftKey) {
		e.preventDefault();
		send();
	}
});

input.addEventListener('input', autoResize);

function send(): void {
	const text = input.value.trim();
	if (!text || !getChatId()) return;
	vscode.postMessage({ type: 'send', text });
	input.value = '';
	autoResize();
	showTyping();
}

function autoResize(): void {
	input.style.height = 'auto';
	input.style.height = Math.min(120, input.scrollHeight) + 'px';
}

// ── Host → webview ─────────────────────────────────────────────

window.addEventListener('message', ev => {
	handleHostMessage(ev.data);
});

vscode.postMessage({ type: 'ready' });
