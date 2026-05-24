import { useState } from 'react';
import type { TokenMeta } from '../../store';

export function TokenBadge({ tokens }: { tokens: TokenMeta }) {
	const [open, setOpen] = useState(false);

	const total = (tokens.input_tokens ?? 0) + (tokens.output_tokens ?? 0);
	if (!total) return null;

	const rows: Array<{ label: string; value: number; color: string }> = [
		{ label: 'Input',  value: tokens.input_tokens  ?? 0, color: '#569cd6' },
		{ label: 'Output', value: tokens.output_tokens ?? 0, color: '#4ec9b0' },
	];
	if (tokens.cache_read_tokens)     rows.push({ label: 'Cache ↩', value: tokens.cache_read_tokens,     color: '#c586c0' });
	if (tokens.cache_creation_tokens) rows.push({ label: 'Cache ↑', value: tokens.cache_creation_tokens, color: '#dcdcaa' });
	if (tokens.thinking_tokens)       rows.push({ label: 'Thinking', value: tokens.thinking_tokens,      color: '#ce9178' });

	const grand = rows.reduce((s, r) => s + r.value, 0);

	return (
		<div className="relative inline-block">
			<button
				type="button"
				onClick={(e) => { e.stopPropagation(); setOpen(v => !v); }}
				title="Token usage"
				className="inline-flex items-center gap-1 typography-meta tabular-nums px-2 py-0.5 rounded-full transition-colors hover:bg-[var(--interactive-hover)]"
				style={{
					color:      'var(--muted-foreground)',
					background: open ? 'var(--interactive-active)' : 'var(--surface-subtle)',
					border:     '1px solid var(--border)',
				}}
			>
				<span className="opacity-70">⌬</span>
				<span>{formatTok(total)}</span>
			</button>
			{open && (
				<div
					className="absolute left-0 top-full mt-1 z-10 min-w-[220px] rounded-lg p-2 space-y-1.5"
					style={{
						background: 'var(--surface-elevated)',
						color:      'var(--surface-elevated-foreground)',
						border:     '1px solid var(--border)',
						boxShadow:  '0 4px 12px rgba(0,0,0,0.18)',
					}}
					onClick={(e) => e.stopPropagation()}
				>
					{rows.map(r => (
						<div key={r.label} className="flex items-center gap-2 typography-meta">
							<span className="w-16 flex-shrink-0" style={{ color: 'var(--muted-foreground)' }}>{r.label}</span>
							<div className="flex-1 h-1.5 rounded-full overflow-hidden" style={{ background: 'var(--surface-subtle)' }}>
								<div className="h-full rounded-full" style={{ width: `${grand > 0 ? (r.value / grand) * 100 : 0}%`, background: r.color }} />
							</div>
							<span className="tabular-nums w-12 text-right">{formatTok(r.value)}</span>
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
