// Message rendering, typing bubble, scrolling, and DOM helpers.

const messagesEl = document.getElementById('messages')!;
const statusEl   = document.getElementById('status')!;

let typingEl: HTMLDivElement | null = null;

export function setStatus(state: string): void {
	statusEl.textContent = state;
	statusEl.className   = `status ${state}`;
}

export function renderMessage(msg: { role?: string; text?: string }): HTMLDivElement {
	const el = document.createElement('div');
	el.className   = `msg ${msg.role || 'assistant'}`;
	el.textContent = msg.text || '';
	if (typingEl && typingEl.parentNode === messagesEl) {
		messagesEl.insertBefore(el, typingEl);
	} else {
		messagesEl.appendChild(el);
	}
	scrollToBottom();
	return el;
}

export function appendSystem(text: string): void {
	const el = document.createElement('div');
	el.className   = 'msg system';
	el.textContent = text;
	if (typingEl && typingEl.parentNode === messagesEl) {
		messagesEl.insertBefore(el, typingEl);
	} else {
		messagesEl.appendChild(el);
	}
	scrollToBottom();
}

export function renderAll(messages: Array<{ role?: string; text?: string }>): void {
	messagesEl.innerHTML = '';
	typingEl = null;
	for (const m of messages) renderMessage(m);
	scrollToBottom();
}

export function clearMessagesDom(): void {
	messagesEl.innerHTML = '';
	typingEl = null;
}

export function showTyping(): void {
	if (typingEl && typingEl.parentNode === messagesEl) {
		scrollToBottom();
		return;
	}
	typingEl = document.createElement('div');
	typingEl.className = 'msg assistant typing-bubble';
	typingEl.setAttribute('aria-label', 'Lis is typing');
	typingEl.innerHTML = '<span></span><span></span><span></span>';
	messagesEl.appendChild(typingEl);
	scrollToBottom();
}

export function hideTyping(): void {
	if (typingEl?.parentNode) typingEl.parentNode.removeChild(typingEl);
	typingEl = null;
}

export function scrollToBottom(): void {
	messagesEl.scrollTop = messagesEl.scrollHeight;
}
