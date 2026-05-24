// Frame dispatch — routes incoming frames to the appropriate handler.

import { appendSystem, hideTyping, renderMessage, renderAll, showTyping, setStatus } from './renderer';

interface Message {
	id:        string;
	role:      string;
	text:      string;
	timestamp: string;
}

let chatId: string | null = null;
const messages: Message[] = [];
const knownMessageIds = new Set<string>();

export function getChatId(): string | null { return chatId; }

export function handleHostMessage(msg: { type: string; [key: string]: unknown }): void {
	switch (msg.type) {
		case 'init':  chatId = msg.chatId as string; break;
		case 'state': setStatus(msg.state as string); break;
		case 'frame': handleFrame(msg.frame as { type: string; [key: string]: unknown }); break;
	}
}

function handleFrame(frame: { type: string; [key: string]: unknown }): void {
	switch (frame.type) {
		case 'message':
			if (frame.role === 'assistant') hideTyping();
			appendMessage({
				id:        frame.id as string,
				role:      frame.role as string,
				text:      frame.text as string,
				timestamp: frame.timestamp as string,
			});
			break;

		case 'typing':         showTyping(); break;
		case 'typing_stopped': hideTyping(); break;

		case 'chat_history':
			replaceHistory(frame.messages as Message[]);
			break;

		case 'error':
			hideTyping();
			appendSystem(`error: ${frame.error}`);
			break;
	}
}

function appendMessage(msg: Message): void {
	if (knownMessageIds.has(msg.id)) return;
	if (!hasText(msg)) return;
	knownMessageIds.add(msg.id);
	messages.push(msg);
	renderMessage(msg);
}

function replaceHistory(list: Message[]): void {
	messages.length = 0;
	knownMessageIds.clear();
	hideTyping();
	for (const m of list) {
		if (!hasText(m)) continue;
		messages.push(m);
		knownMessageIds.add(m.id);
	}
	renderAll(messages);
}

function hasText(msg: { text?: string }): boolean {
	return typeof msg.text === 'string' && msg.text.trim().length > 0;
}
