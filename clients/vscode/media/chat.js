// Webview runtime — owns the chat UI and bridges to the extension host.
// No imports — runs as a classic script inside the webview iframe.

(function () {
	const vscode = acquireVsCodeApi();

	const statusEl   = document.getElementById('status');
	const chatSelect = document.getElementById('chat-select');
	const newChatBtn = document.getElementById('new-chat');
	const messagesEl = document.getElementById('messages');
	const typingEl   = document.getElementById('typing');
	const form       = document.getElementById('composer');
	const input      = document.getElementById('input');

	let currentChatId = null;
	const chats = new Map(); // chat_id -> { name, messages: [{id, role, text, timestamp}] }
	const knownMessageIds = new Set();

	// ── Wire up UI ─────────────────────────────────────────────────

	form.addEventListener('submit', e => {
		e.preventDefault();
		sendCurrent();
	});

	input.addEventListener('keydown', e => {
		if (e.key === 'Enter' && !e.shiftKey) {
			e.preventDefault();
			sendCurrent();
		}
	});

	input.addEventListener('input', autoResize);

	newChatBtn.addEventListener('click', () => {
		vscode.postMessage({ type: 'newChat' });
	});

	chatSelect.addEventListener('change', () => {
		currentChatId = chatSelect.value;
		renderMessages();
		vscode.postMessage({ type: 'requestHistory', chatId: currentChatId });
	});

	function sendCurrent() {
		const text = input.value.trim();
		if (!text || !currentChatId) return;
		vscode.postMessage({ type: 'send', chatId: currentChatId, text });
		input.value = '';
		autoResize();
	}

	function autoResize() {
		input.style.height = 'auto';
		input.style.height = Math.min(120, input.scrollHeight) + 'px';
	}

	// ── Extension → Webview messages ───────────────────────────────

	window.addEventListener('message', ev => {
		const msg = ev.data;
		switch (msg.type) {
			case 'state':       setStatus(msg.state); break;
			case 'chatList':    setChatList(msg.chats); break;
			case 'currentChat': setCurrentChat(msg.chatId); break;
			case 'frame':       handleFrame(msg.frame); break;
		}
	});

	// Signal we're ready so the extension can push initial state.
	vscode.postMessage({ type: 'ready' });

	// ── Frame handling ─────────────────────────────────────────────

	function handleFrame(frame) {
		switch (frame.type) {
			case 'message':
				appendMessage(frame.chat_id, {
					id:        frame.id,
					role:      frame.role,
					text:      frame.text,
					timestamp: frame.timestamp,
				});
				if (frame.chat_id === currentChatId && frame.role === 'assistant') hideTyping();
				break;

			case 'typing':
				if (frame.chat_id === currentChatId) showTyping();
				break;

			case 'typing_stopped':
				if (frame.chat_id === currentChatId) hideTyping();
				break;

			case 'chat_list':
				setChatList(frame.chats);
				break;

			case 'chat_history':
				replaceHistory(frame.chat_id, frame.messages);
				break;

			case 'error':
				appendSystem(`error: ${frame.error}`);
				hideTyping();
				break;
		}
	}

	function appendMessage(chatId, msg) {
		if (knownMessageIds.has(msg.id)) return;
		knownMessageIds.add(msg.id);

		const chat = ensureChat(chatId);
		chat.messages.push(msg);

		if (chatId === currentChatId) renderMessage(msg);
	}

	function replaceHistory(chatId, messages) {
		const chat = ensureChat(chatId);
		chat.messages = messages.slice();
		for (const m of messages) knownMessageIds.add(m.id);
		if (chatId === currentChatId) renderMessages();
	}

	function ensureChat(chatId) {
		let chat = chats.get(chatId);
		if (!chat) {
			chat = { name: chatId, messages: [] };
			chats.set(chatId, chat);
			refreshChatSelect();
		}
		return chat;
	}

	// ── Rendering ──────────────────────────────────────────────────

	function setStatus(state) {
		statusEl.textContent = state;
		statusEl.className   = `status ${state}`;
	}

	function setChatList(list) {
		for (const c of list) {
			if (!chats.has(c.chat_id)) chats.set(c.chat_id, { name: c.name || c.chat_id, messages: [] });
		}
		refreshChatSelect();
	}

	function setCurrentChat(chatId) {
		ensureChat(chatId);
		currentChatId = chatId;
		chatSelect.value = chatId;
		renderMessages();
		vscode.postMessage({ type: 'requestHistory', chatId });
	}

	function refreshChatSelect() {
		const sorted = [...chats.entries()].sort(([a], [b]) => a.localeCompare(b));
		chatSelect.innerHTML = '';
		for (const [id, chat] of sorted) {
			const opt = document.createElement('option');
			opt.value = id;
			opt.textContent = chat.name || id;
			chatSelect.appendChild(opt);
		}
		if (currentChatId) chatSelect.value = currentChatId;
	}

	function renderMessages() {
		messagesEl.innerHTML = '';
		const chat = currentChatId ? chats.get(currentChatId) : null;
		if (!chat) return;
		for (const m of chat.messages) renderMessage(m);
		scrollToBottom();
	}

	function renderMessage(msg) {
		const el = document.createElement('div');
		el.className   = `msg ${msg.role || 'assistant'}`;
		el.textContent = msg.text || '';
		messagesEl.appendChild(el);
		scrollToBottom();
	}

	function appendSystem(text) {
		const el = document.createElement('div');
		el.className   = 'msg system';
		el.textContent = text;
		messagesEl.appendChild(el);
		scrollToBottom();
	}

	function showTyping() { typingEl.hidden = false; scrollToBottom(); }
	function hideTyping() { typingEl.hidden = true; }

	function scrollToBottom() {
		messagesEl.scrollTop = messagesEl.scrollHeight;
	}
})();
