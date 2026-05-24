import { useState } from 'preact/hooks';
import type { TokenMeta } from '../../state';

export function TokenBadge({ tokens }: { tokens: TokenMeta }) {
	const [open, setOpen] = useState(false);

	const total = (tokens.input_tokens ?? 0) + (tokens.output_tokens ?? 0);
	if (!total) return null;

	const rows: Array<{ label: string; value: number; color: string }> = [
		{ label: 'Input',   value: tokens.input_tokens  ?? 0, color: '#569cd6' },
		{ label: 'Output',  value: tokens.output_tokens ?? 0, color: '#4ec9b0' },
	];
	if (tokens.cache_read_tokens)     rows.push({ label: 'Cache ↩', value: tokens.cache_read_tokens,     color: '#c586c0' });
	if (tokens.cache_creation_tokens) rows.push({ label: 'Cache ↑', value: tokens.cache_creation_tokens, color: '#dcdcaa' });
	if (tokens.thinking_tokens)       rows.push({ label: 'Thinking', value: tokens.thinking_tokens,      color: '#ce9178' });

	const grand = rows.reduce((s, r) => s + r.value, 0);

	return (
		<div class="token-badge-wrap">
			<button
				type="button"
				class={`token-badge${open ? ' token-badge--open' : ''}`}
				onClick={e => { e.stopPropagation(); setOpen(v => !v); }}
				title="Token usage"
			>
				{formatTok(total)}
			</button>
			{open && (
				<div class="token-panel" onClick={e => e.stopPropagation()}>
					{rows.map(r => (
						<div key={r.label} class="token-row">
							<span class="token-label">{r.label}</span>
							<div class="token-bar-track">
								<div
									class="token-bar-fill"
									style={{ width: `${grand > 0 ? (r.value / grand) * 100 : 0}%`, background: r.color }}
								/>
							</div>
							<span class="token-value">{formatTok(r.value)}</span>
						</div>
					))}
				</div>
			)}
		</div>
	);
}

function formatTok(n: number): string {
	if (n >= 1_000_000) return `${(n / 1_000_000).toFixed(1)}M`;
	if (n >= 1_000)     return `${(n / 1_000).toFixed(n >= 10_000 ? 0 : 1)}k`;
	return `${n}`;
}
