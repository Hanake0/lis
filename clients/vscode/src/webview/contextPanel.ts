// Context-window panel — matches Claude Code's layout.
// Total/budget on top (single progress bar), per-segment table below, model + effort
// footer. The TOTAL is the authoritative API number from the last assistant message;
// segment numbers are locally tokenized (the API doesn't break them down).

import { vscode } from './vscodeApi';
import { getChatId } from './dispatcher';

interface Segment { name: string; tokens: number; }

const SEGMENT_COLORS: Record<string, string> = {
	'Messages':      '#569cd6',
	'Tools':         '#dcdcaa',
	'System prompt': '#4ec9b0',
	'Summaries':     '#c586c0',
	'Free space':    'transparent',
};
const FALLBACK_COLOR = '#888';
const FREE_SPACE_NAME = 'Free space';

let panelEl: HTMLDivElement | null = null;
let isOpen  = false;

export function ensurePanel(): HTMLDivElement {
	if (panelEl) return panelEl;
	panelEl = document.createElement('div');
	panelEl.className = 'context-panel hidden';
	panelEl.innerHTML = `
		<div class="context-header">
			<span class="context-title">Context window</span>
			<span class="context-total-line">
				<span class="context-total"></span>
				<span class="context-of">/</span>
				<span class="context-budget"></span>
				<span class="context-pct"></span>
			</span>
			<button class="context-close" type="button" aria-label="Close">✕</button>
		</div>
		<div class="context-bar"><div class="context-bar-fill"></div></div>
		<div class="context-body">
			<div class="context-loading">Loading…</div>
		</div>
		<div class="context-footer">
			<span class="context-model"></span>
			<span class="context-effort"></span>
		</div>
	`;
	const composer = document.getElementById('composer')!;
	composer.parentElement!.insertBefore(panelEl, composer);
	panelEl.querySelector('.context-close')!.addEventListener('click', closePanel);
	return panelEl;
}

export function toggleContextPanel(): void {
	const el = ensurePanel();
	if (isOpen) { closePanel(); return; }
	isOpen = true;
	el.classList.remove('hidden');
	el.querySelector('.context-body')!.innerHTML = '<div class="context-loading">Loading…</div>';
	const chatId = getChatId();
	if (!chatId) {
		el.querySelector('.context-body')!.innerHTML = '<div class="context-error">No chat selected.</div>';
		return;
	}
	vscode.postMessage({ type: 'send_frame', frame: { type: 'get_context', chat_id: chatId } });
}

function closePanel(): void {
	if (!panelEl) return;
	panelEl.classList.add('hidden');
	isOpen = false;
}

export function renderContext(
	total:    number,
	budget:   number,
	segments: Segment[],
	model:    string,
	effort?:  string,
): void {
	const el = ensurePanel();
	if (!isOpen) return;

	const pct = budget > 0 ? Math.min(100, (total / budget) * 100) : 0;
	(el.querySelector('.context-total')  as HTMLElement).textContent = formatTokens(total);
	(el.querySelector('.context-budget') as HTMLElement).textContent = formatTokens(budget);
	(el.querySelector('.context-pct')    as HTMLElement).textContent = `(${pct.toFixed(1)}%)`;
	(el.querySelector('.context-bar-fill') as HTMLElement).style.width = `${pct}%`;
	(el.querySelector('.context-model')  as HTMLElement).textContent = model;
	(el.querySelector('.context-effort') as HTMLElement).textContent = effort ? `· ${effort}` : '';

	const body = el.querySelector('.context-body') as HTMLElement;
	body.innerHTML = '';

	// Sort segments descending by tokens, but pin "Free space" to the bottom.
	const sorted = [...segments].sort((a, b) => {
		if (a.name === FREE_SPACE_NAME) return  1;
		if (b.name === FREE_SPACE_NAME) return -1;
		return b.tokens - a.tokens;
	});

	const table = document.createElement('table');
	table.className = 'context-table';
	for (const seg of sorted) {
		const pctOfBudget = budget > 0 ? (seg.tokens / budget) * 100 : 0;
		const color = SEGMENT_COLORS[seg.name] ?? FALLBACK_COLOR;
		const tr = document.createElement('tr');
		if (seg.name === FREE_SPACE_NAME) tr.classList.add('context-free');
		tr.innerHTML = `
			<td class="context-swatch"><span style="background:${color}"></span></td>
			<td class="context-name">${escapeHtml(seg.name)}</td>
			<td class="context-tokens">${formatTokens(seg.tokens)}</td>
			<td class="context-pct-cell">${pctOfBudget.toFixed(1)}%</td>
		`;
		table.appendChild(tr);
	}
	body.appendChild(table);
}

function formatTokens(n: number): string {
	if (n >= 1_000_000) return (n / 1_000_000).toFixed(2) + 'M';
	if (n >= 1_000)     return (n / 1_000).toFixed(1) + 'k';
	return String(n);
}

function escapeHtml(s: string): string {
	return s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}
