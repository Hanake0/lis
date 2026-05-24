// Context-usage panel — shows the per-segment token breakdown of the prompt
// the next API call would carry. Toggled via a button in the composer controls.

import { vscode } from './vscodeApi';
import { getChatId } from './dispatcher';

interface Segment { name: string; tokens: number; }

const SEGMENT_COLORS: Record<string, string> = {
	'System prompt':      '#4ec9b0',
	'Memory / summaries': '#c586c0',
	'Messages':           '#569cd6',
	'Tools':              '#dcdcaa',
};
const FALLBACK_COLOR = '#888';

let panelEl:    HTMLDivElement | null = null;
let isOpen = false;

export function ensurePanel(): HTMLDivElement {
	if (panelEl) return panelEl;
	panelEl = document.createElement('div');
	panelEl.className = 'context-panel hidden';
	panelEl.innerHTML = `
		<div class="context-header">
			<span class="context-title">Context window</span>
			<button class="context-close" type="button" aria-label="Close">✕</button>
		</div>
		<div class="context-body">
			<div class="context-loading">Loading…</div>
		</div>
	`;
	const composer = document.getElementById('composer')!;
	composer.parentElement!.insertBefore(panelEl, composer);
	panelEl.querySelector('.context-close')!.addEventListener('click', closePanel);
	return panelEl;
}

export function toggleContextPanel(): void {
	const el = ensurePanel();
	if (isOpen) {
		closePanel();
		return;
	}
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

export function renderContext(total: number, budget: number, segments: Segment[]): void {
	const el = ensurePanel();
	if (!isOpen) return; // ignore stale responses if the user closed it

	const body = el.querySelector('.context-body')! as HTMLElement;
	const pctOfBudget = budget > 0 ? Math.min(100, (total / budget) * 100) : 0;

	// Build stacked bar (relative to budget so unused space is visible).
	const bar = document.createElement('div');
	bar.className = 'context-bar';
	for (const seg of segments) {
		if (seg.tokens <= 0) continue;
		const slice = document.createElement('div');
		slice.className = 'context-bar-slice';
		slice.style.background = SEGMENT_COLORS[seg.name] ?? FALLBACK_COLOR;
		slice.style.flexGrow = String(seg.tokens);
		slice.title = `${seg.name}: ${formatTokens(seg.tokens)}`;
		bar.appendChild(slice);
	}
	if (budget > total) {
		const remaining = document.createElement('div');
		remaining.className = 'context-bar-slice context-bar-remaining';
		remaining.style.flexGrow = String(budget - total);
		remaining.title = `Free: ${formatTokens(budget - total)}`;
		bar.appendChild(remaining);
	}

	// Header line with total / budget.
	const header = document.createElement('div');
	header.className = 'context-summary';
	header.innerHTML = `
		<span class="context-total">${formatTokens(total)}</span>
		<span class="context-of">of</span>
		<span class="context-budget">${formatTokens(budget)}</span>
		<span class="context-pct">(${pctOfBudget.toFixed(1)}%)</span>
	`;

	// Table.
	const table = document.createElement('table');
	table.className = 'context-table';
	const totalForPct = total > 0 ? total : 1;
	for (const seg of segments) {
		const tr = document.createElement('tr');
		const pct = (seg.tokens / totalForPct) * 100;
		const color = SEGMENT_COLORS[seg.name] ?? FALLBACK_COLOR;
		tr.innerHTML = `
			<td class="context-swatch"><span style="background:${color}"></span></td>
			<td class="context-name">${escapeHtml(seg.name)}</td>
			<td class="context-tokens">${formatTokens(seg.tokens)}</td>
			<td class="context-pct-cell">${pct.toFixed(1)}%</td>
		`;
		table.appendChild(tr);
	}

	body.innerHTML = '';
	body.appendChild(header);
	body.appendChild(bar);
	body.appendChild(table);
}

function formatTokens(n: number): string {
	if (n >= 1_000_000) return (n / 1_000_000).toFixed(2) + 'M';
	if (n >= 1_000)     return (n / 1_000).toFixed(1) + 'k';
	return String(n);
}

function escapeHtml(s: string): string {
	return s
		.replace(/&/g, '&amp;')
		.replace(/</g, '&lt;')
		.replace(/>/g, '&gt;');
}
