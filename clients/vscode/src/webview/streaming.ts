// Streaming chunk accumulation and progressive text rendering.

import { hideTyping, scrollToBottom } from './renderer';

const messagesEl = document.getElementById('messages')!;

const streamingElements = new Map<string, HTMLDivElement>();
const thinkingElements  = new Map<string, HTMLDivElement>();

export function handleChunk(id: string, delta: string, done: boolean): void {
	if (done) {
		// Keep in map — finalizeStreamedMessage will remove both map entry and DOM element.
		return;
	}

	let el = streamingElements.get(id);
	if (!el) {
		hideTyping();
		el = document.createElement('div');
		el.className = 'msg assistant streaming';
		el.dataset.streamId = id;
		messagesEl.appendChild(el);
		streamingElements.set(id, el);
	}

	el.textContent += delta;
	scrollToBottom();
}

export function handleThinkingChunk(id: string, delta: string, done: boolean): void {
	if (done) {
		const el = thinkingElements.get(id);
		if (el) el.classList.add('collapsed');
		thinkingElements.delete(id);
		return;
	}

	hideTyping();

	let el = thinkingElements.get(id);
	if (!el) {
		el = document.createElement('div');
		el.className = 'msg thinking-block';
		el.dataset.streamId = id;

		const label = document.createElement('div');
		label.className = 'thinking-label';
		label.textContent = 'Thinking...';
		label.addEventListener('click', () => el!.classList.toggle('collapsed'));
		el.appendChild(label);

		const content = document.createElement('div');
		content.className = 'thinking-content';
		el.appendChild(content);

		messagesEl.appendChild(el);
		thinkingElements.set(id, el);
	}

	const content = el.querySelector('.thinking-content');
	if (content) content.textContent += delta;
	scrollToBottom();
}

/**
 * Annotates the most recent thinking block (i.e. the one for the assistant turn
 * that just finished) with the total thinking token count from the final message
 * frame. The message frame's id is unrelated to the streaming-chunk id so we
 * target by recency rather than by id, mirroring finalizeStreamedMessage.
 */
export function setThinkingTokens(tokens: number): void {
	const blocks = messagesEl.querySelectorAll<HTMLDivElement>('.thinking-block');
	const el = blocks[blocks.length - 1];
	if (!el) return;
	let pill = el.querySelector<HTMLSpanElement>('.thinking-tokens');
	if (!pill) {
		pill = document.createElement('span');
		pill.className = 'thinking-tokens';
		el.querySelector('.thinking-label')?.appendChild(pill);
	}
	pill.textContent = formatTokens(tokens);
	pill.title = `${tokens} thinking tokens`;
}

function formatTokens(n: number): string {
	if (n >= 1000) return `${(n / 1000).toFixed(n >= 10000 ? 0 : 1)}k`;
	return `${n}`;
}

export function finalizeStreamedMessage(_id: string, _text: string): void {
	// Remove ALL active streaming elements — the final message frame may have
	// a different ID than the streaming chunks (backend generates IDs separately).
	for (const [sid, el] of streamingElements) {
		el.remove();
		streamingElements.delete(sid);
	}
}
