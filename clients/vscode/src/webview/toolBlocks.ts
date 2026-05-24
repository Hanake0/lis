// Tool call/result collapsible blocks.

import { scrollToBottom } from './renderer';

const messagesEl = document.getElementById('messages')!;
const toolElements = new Map<string, HTMLDivElement>();

export function handleToolStart(id: string, toolName: string, toolArgs?: string): void {
	const el = document.createElement('div');
	el.className = 'tool-block';
	el.dataset.toolId = id;

	const displayName = formatToolName(toolName);
	const header = document.createElement('div');
	header.className = 'tool-header';
	header.innerHTML = `<span class="tool-icon">⚙</span> <span class="tool-name">${escapeHtml(displayName)}</span> <span class="tool-status running">running</span>`;
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
	toolElements.set(id, el);
	scrollToBottom();
}

export function handleToolResult(id: string, _toolName: string, result?: string, error?: string): void {
	const el = toolElements.get(id);
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
	toolElements.delete(id);
	scrollToBottom();
}

function formatToolName(name: string): string {
	const parts = name.split('_');
	if (parts.length >= 2) return `${parts[0]}:${parts.slice(1).join('_')}`;
	return name;
}

function escapeHtml(s: string): string {
	return s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}
