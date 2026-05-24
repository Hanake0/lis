// Webview runtime — one chat per panel. The host announces our chat_id via the
// 'init' message, after which we render only frames matching that id.

(function () {
	const vscode = acquireVsCodeApi();

	const statusEl   = document.getElementById('status');
	const messagesEl = document.getElementById('messages');
	const form       = document.getElementById('composer');
	const input      = document.getElementById('input');

	let chatId = null;
	let typingEl = null;
	const messages = [];
	const knownMessageIds = new Set();

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

	function send() {
		const text = input.value.trim();
		if (!text || !chatId) return;
		vscode.postMessage({ type: 'send', text });
		input.value = '';
		autoResize();
		// Optimistic: show the typing bubble immediately so the user sees the
		// assistant is "thinking" without waiting for the server's typing frame.
		showTyping();
	}

	function autoResize() {
		input.style.height = 'auto';
		input.style.height = Math.min(120, input.scrollHeight) + 'px';
	}

	// ── Host → webview ─────────────────────────────────────────────

	window.addEventListener('message', ev => {
		const msg = ev.data;
		switch (msg.type) {
			case 'init':  chatId = msg.chatId; break;
			case 'state': setStatus(msg.state); break;
			case 'frame': handleFrame(msg.frame); break;
		}
	});

	vscode.postMessage({ type: 'ready' });

	// ── Frame handling ─────────────────────────────────────────────

	function handleFrame(frame) {
		switch (frame.type) {
			case 'message':
				// Hide typing BEFORE the message renders so the bubble doesn't appear
				// briefly below the new message.
				if (frame.role === 'assistant') hideTyping();
				appendMessage({
					id:        frame.id,
					role:      frame.role,
					text:      frame.text,
					timestamp: frame.timestamp,
				});
				break;

			case 'typing':         showTyping(); break;
			case 'typing_stopped': hideTyping(); break;

			case 'chat_history':
				replaceHistory(frame.messages);
				break;

			case 'error':
				hideTyping();
				appendSystem(`error: ${frame.error}`);
				break;
		}
	}

	function appendMessage(msg) {
		if (knownMessageIds.has(msg.id)) return;
		if (!hasText(msg)) return;
		knownMessageIds.add(msg.id);
		messages.push(msg);
		renderMessage(msg);
	}

	function replaceHistory(list) {
		messages.length = 0;
		knownMessageIds.clear();
		hideTyping();
		for (const m of list) {
			if (!hasText(m)) continue;
			messages.push(m);
			knownMessageIds.add(m.id);
		}
		renderAll();
	}

	function hasText(msg) {
		return typeof msg.text === 'string' && msg.text.trim().length > 0;
	}

	// ── Rendering ──────────────────────────────────────────────────

	function setStatus(state) {
		statusEl.textContent = state;
		statusEl.className   = `status ${state}`;
	}

	function renderAll() {
		messagesEl.innerHTML = '';
		typingEl = null; // detached with the parent's children
		for (const m of messages) renderMessage(m);
		scrollToBottom();
	}

	function renderMessage(msg) {
		// If a typing bubble is at the end, the new message belongs above it; reorder.
		const el = document.createElement('div');
		el.className   = `msg ${msg.role || 'assistant'}`;
		el.textContent = msg.text || '';
		if (typingEl && typingEl.parentNode === messagesEl) {
			messagesEl.insertBefore(el, typingEl);
		} else {
			messagesEl.appendChild(el);
		}
		scrollToBottom();
	}

	function appendSystem(text) {
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

	function showTyping() {
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

	function hideTyping() {
		if (typingEl?.parentNode) typingEl.parentNode.removeChild(typingEl);
		typingEl = null;
	}

	function scrollToBottom() {
		messagesEl.scrollTop = messagesEl.scrollHeight;
	}
})();
