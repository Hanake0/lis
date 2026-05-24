import { useEffect } from 'preact/hooks';
import { contextOpen, contextData, chatId } from '../state';
import { vscode } from '../vscodeApi';

const SEGMENT_COLORS: Record<string, string> = {
	'Messages':      '#569cd6',
	'Tools':         '#dcdcaa',
	'System prompt': '#4ec9b0',
	'Summaries':     '#c586c0',
	'Free space':    'transparent',
};

export function ContextPanel() {
	const open = contextOpen.value;
	const data = contextData.value;

	// Fetch context when panel opens
	useEffect(() => {
		if (!open) return;
		contextData.value = null;
		const id = chatId.value;
		if (id) vscode.postMessage({ type: 'send_frame', frame: { type: 'get_context', chat_id: id } });
	}, [open]);

	if (!open) return null;

	const pct = data && data.budget > 0 ? Math.min(100, (data.total / data.budget) * 100) : 0;

	const sorted = data
		? [...data.segments].sort((a, b) => {
			if (a.name === 'Free space') return  1;
			if (b.name === 'Free space') return -1;
			return b.tokens - a.tokens;
		})
		: [];

	return (
		<div class="context-panel">
			<div class="context-header">
				<span class="context-title">Context window</span>
				{data && (
					<span class="context-summary">
						<strong>{fmtTok(data.total)}</strong>
						<span class="context-sep">/</span>
						{fmtTok(data.budget)}
						<span class="context-pct">({pct.toFixed(1)}%)</span>
					</span>
				)}
				<button type="button" class="context-close" onClick={() => { contextOpen.value = false; }}>✕</button>
			</div>

			{data ? (
				<>
					<div class="context-bar">
						<div class="context-bar-fill" style={{ width: `${pct}%` }} />
					</div>
					<table class="context-table">
						<tbody>
							{sorted.map(seg => (
								<tr key={seg.name} class={seg.name === 'Free space' ? 'context-free' : ''}>
									<td class="context-swatch">
										<span style={{ background: SEGMENT_COLORS[seg.name] ?? '#888' }} />
									</td>
									<td class="context-name">{seg.name}</td>
									<td class="context-tokens">{fmtTok(seg.tokens)}</td>
									<td class="context-pct-cell">{fmtPct(seg.tokens, data.budget)}</td>
								</tr>
							))}
						</tbody>
					</table>
					<div class="context-footer">
						<span>{data.model}</span>
						{data.effort && <span class="context-effort">· {data.effort}</span>}
					</div>
				</>
			) : (
				<div class="context-loading">Loading…</div>
			)}
		</div>
	);
}

function fmtTok(n: number): string {
	// Backend emits -1 when count_tokens failed for that segment — never substitute a
	// local estimate; render an explicit em-dash so the user sees the gap.
	if (n < 0)          return '—';
	if (n >= 1_000_000) return `${(n / 1_000_000).toFixed(2)}M`;
	if (n >= 1_000)     return `${(n / 1_000).toFixed(1)}k`;
	return `${n}`;
}

function fmtPct(tokens: number, budget: number): string {
	if (tokens < 0)   return '—';
	if (budget <= 0) return '0%';
	return `${((tokens / budget) * 100).toFixed(1)}%`;
}
