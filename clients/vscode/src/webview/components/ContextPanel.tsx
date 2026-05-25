import { useEffect } from 'react';
import { useChatStore } from '../store';
import { LisRpcClient } from '../lib/lis/rpc-client';
import type { ContextSegment } from '../../types';
import { Icon } from './icon/Icon';

/** Wire shape returned by `GET /api/chats/{id}/context`. */
interface ContextWire {
	total:    number;
	budget:   number;
	segments: ContextSegment[];
	model:    string;
	effort?:  string;
}

const SEGMENT_COLORS: Record<string, string> = {
	'Messages':          '#569cd6',
	'Tools':             '#dcdcaa',
	'System prompt':     '#4ec9b0',
	'Memory files':      '#c586c0',
	'Summaries':         '#9b59b6',
	'Compaction buffer': '#888',
	'Free space':        'transparent',
};

function colorForSegment(name: string): string {
	if (SEGMENT_COLORS[name] !== undefined) return SEGMENT_COLORS[name];
	let h = 0;
	for (let i = 0; i < name.length; i++) h = (h * 31 + name.charCodeAt(i)) | 0;
	const hue = Math.abs(h) % 360;
	return `hsl(${hue}, 45%, 55%)`;
}

export function ContextPanel() {
	const open    = useChatStore(s => s.contextOpen);
	const data    = useChatStore(s => s.contextData);
	const chatId  = useChatStore(s => s.chatId);
	const setOpen = useChatStore(s => s.setContextOpen);
	const setData = useChatStore(s => s.setContextData);

	useEffect(() => {
		if (!open || !chatId) return;
		setData(null);
		let cancelled = false;
		void (async () => {
			try {
				const ctx = await LisRpcClient.instance().getChatContext(chatId) as ContextWire;
				if (!cancelled) setData(ctx);
			} catch { /* ignore — closing the panel cancels via `cancelled` flag */ }
		})();
		return () => { cancelled = true; };
	}, [open, chatId, setData]);

	function refresh() {
		if (!chatId) return;
		setData(null);
		void (async () => {
			try {
				const ctx = await LisRpcClient.instance().getChatContext(chatId, { force: true }) as ContextWire;
				setData(ctx);
			} catch { /* ignore */ }
		})();
	}

	if (!open) return null;

	const pct = data && data.budget > 0 ? Math.min(100, (data.total / data.budget) * 100) : 0;
	const tailRank = (name: string) =>
		name === 'Free space' ? 2 : name === 'Compaction buffer' ? 1 : 0;
	const sorted = data
		? [...data.segments].sort((a, b) => {
			const ra = tailRank(a.name), rb = tailRank(b.name);
			if (ra !== rb) return ra - rb;
			return b.tokens - a.tokens;
		})
		: [];

	return (
		<div
			className="absolute right-4 top-12 z-30 w-80 max-w-[90vw] rounded-xl p-3 space-y-2"
			style={{
				background: 'var(--surface-elevated)',
				color:      'var(--surface-elevated-foreground)',
				border:     '1px solid var(--border)',
				boxShadow:  '0 8px 24px rgba(0,0,0,0.28)',
			}}
		>
			<div className="flex items-center gap-2">
				<span className="typography-ui-header font-medium flex-1">Context window</span>
				<button
					type="button"
					onClick={refresh}
					title="Force refresh"
					className="p-1 rounded hover:bg-[var(--interactive-hover)]"
					style={{ color: 'var(--muted-foreground)' }}
				>
					<Icon name="refresh" className="w-3.5 h-3.5" />
				</button>
				<button
					type="button"
					onClick={() => setOpen(false)}
					title="Close"
					className="p-1 rounded hover:bg-[var(--interactive-hover)]"
					style={{ color: 'var(--muted-foreground)' }}
				>
					<Icon name="close" className="w-3.5 h-3.5" />
				</button>
			</div>

			{data ? (
				<>
					<div className="flex items-baseline gap-1 typography-meta" style={{ color: 'var(--muted-foreground)' }}>
						<strong style={{ color: 'var(--foreground)' }}>{fmtTok(data.total)}</strong>
						<span>/</span>
						<span>{fmtTok(data.budget)}</span>
						<span className="ml-auto tabular-nums">{pct.toFixed(1)}%</span>
					</div>

					<div className="h-1.5 rounded-full overflow-hidden" style={{ background: 'var(--surface-subtle)' }}>
						<div
							className="h-full transition-all"
							style={{ width: `${pct}%`, background: pct >= 85 ? 'var(--status-error)' : pct >= 65 ? 'var(--status-warning)' : 'var(--status-success)' }}
						/>
					</div>

					<table className="w-full typography-meta">
						<tbody>
							{sorted.map(seg => (
								<tr
									key={seg.name}
									style={{ opacity: seg.name === 'Free space' ? 0.6 : 1 }}
								>
									<td className="py-0.5 pr-1.5 w-3">
										<span className="block w-2 h-2 rounded-sm" style={{ background: colorForSegment(seg.name), border: seg.name === 'Free space' ? '1px dashed var(--border)' : 'none' }} />
									</td>
									<td className="py-0.5 truncate" style={{ color: 'var(--foreground)' }}>{seg.name}</td>
									<td className="py-0.5 px-1 text-right tabular-nums" style={{ color: 'var(--muted-foreground)' }}>{fmtTok(seg.tokens)}</td>
									<td className="py-0.5 text-right tabular-nums w-12" style={{ color: 'var(--muted-foreground)' }}>{fmtPct(seg.tokens, data.budget)}</td>
								</tr>
							))}
						</tbody>
					</table>

					<div className="flex items-center gap-1 typography-meta pt-1" style={{ color: 'var(--muted-foreground)', borderTop: '1px solid var(--border)' }}>
						<span className="pt-1">{data.model}</span>
						{data.effort && <span className="pt-1">· {data.effort}</span>}
					</div>
				</>
			) : (
				<div className="py-4 text-center typography-meta" style={{ color: 'var(--muted-foreground)' }}>Loading…</div>
			)}
		</div>
	);
}

function fmtTok(n: number): string {
	if (n < 0)          return '—';
	if (n >= 1_000_000) return `${(n / 1_000_000).toFixed(2)}M`;
	if (n >= 1_000)     return `${(n / 1_000).toFixed(1)}k`;
	return `${n}`;
}

function fmtPct(tokens: number, budget: number): string {
	if (tokens < 0)  return '—';
	if (budget <= 0) return '0%';
	return `${((tokens / budget) * 100).toFixed(1)}%`;
}
