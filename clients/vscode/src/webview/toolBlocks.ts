// Tool call/result collapsible blocks.

import { scrollToBottom } from './renderer';

const messagesEl = document.getElementById('messages')!;
// Track by id so tool_result + message_tokens can update an existing block — even
// after the call completes (the backfilled token count arrives after the next turn).
const toolBlocks = new Map<string, HTMLDivElement>();

export function handleToolStart(id: string, toolName: string, toolArgs?: string, tokens?: number): void {
	const el = document.createElement('div');
	el.className = 'tool-block';
	el.dataset.toolId = id;

	const displayName = formatToolName(toolName);
	const header = document.createElement('div');
	header.className = 'tool-header';
	header.innerHTML = `<span class="tool-icon">⚙</span> <span class="tool-name">${escapeHtml(displayName)}</span> <span class="tool-tokens"></span> <span class="tool-status running">running</span>`;
	header.addEventListener('click', () => el.classList.toggle('collapsed'));
	el.appendChild(header);

	if (toolArgs && toolArgs !== '{}') {
		const args = document.createElement('pre');
		args.className = 'tool-args';
		try { args.textContent = JSON.stringify(JSON.parse(toolArgs), null, 2); }
		catch { args.textContent = toolArgs; }
		el.appendChild(args);
	}

	messagesEl.appendChild(el);
	toolBlocks.set(id, el);
	if (tokens) setToolTokens(el, tokens);
	scrollToBottom();
}

export function handleToolResult(id: string, _toolName: string, result?: string, error?: string): void {
	const el = toolBlocks.get(id);
	if (!el) return;

	const statusEl = el.querySelector('.tool-status');
	if (statusEl) {
		statusEl.className = error ? 'tool-status error' : 'tool-status done';
		statusEl.textContent = error ? 'error' : 'done';
	}

	const resultEl = document.createElement('pre');
	resultEl.className = error ? 'tool-result error' : 'tool-result';
	resultEl.textContent = error || result || '(no output)';
	el.appendChild(resultEl);

	el.classList.add('collapsed');
	// NOTE: don't remove from toolBlocks — a backfilled message_tokens frame may
	// arrive after the next assistant turn returns, and we want to attach it here.
	scrollToBottom();
}

/**
 * Late-arriving token count for an existing tool block (backfilled from the API
 * input delta after the subsequent assistant turn). Updates the header pill.
 */
export function handleToolTokens(id: string, tokens: number): void {
	const el = toolBlocks.get(id);
	if (!el) return;
	setToolTokens(el, tokens);
}

function setToolTokens(el: HTMLDivElement, tokens: number): void {
	const pill = el.querySelector('.tool-tokens') as HTMLSpanElement | null;
	if (!pill) return;
	pill.textContent = formatTokens(tokens);
	pill.title = `${tokens} tokens`;
}

function formatTokens(n: number): string {
	if (n >= 1000) return `${(n / 1000).toFixed(n >= 10000 ? 0 : 1)}k`;
	return `${n}`;
}

function formatToolName(name: string): string {
	const parts = name.split('_');
	if (parts.length >= 2) return `${parts[0]}:${parts.slice(1).join('_')}`;
	return name;
}

function escapeHtml(s: string): string {
	return s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}
