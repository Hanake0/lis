import { useChatStore } from '../store';
import { Icon } from './icon/Icon';

export function StatusBar() {
	const state       = useChatStore(s => s.connection);
	const contextOpen = useChatStore(s => s.contextOpen);
	const setOpen     = useChatStore(s => s.setContextOpen);
	const contextData = useChatStore(s => s.contextData);

	const label = state === 'connected' ? 'connected' : state === 'connecting' ? 'connecting…' : 'disconnected';
	const dotColor =
		state === 'connected'  ? 'var(--status-success)' :
		state === 'connecting' ? 'var(--status-warning)' :
		                         'var(--status-error)';

	const pct = contextData && contextData.budget > 0
		? (contextData.total / contextData.budget) * 100
		: null;
	const pctColor = pct === null      ? 'var(--muted-foreground)'
		: pct >= 85                    ? 'var(--status-error)'
		: pct >= 65                    ? 'var(--status-warning)'
		: 'var(--status-success)';

	return (
		<div className="flex-shrink-0 px-3 py-1.5" style={{ background: 'var(--background)' }}>
			<div className="flex items-center gap-2 typography-meta" style={{ color: 'var(--muted-foreground)' }}>
				<span className="flex items-center gap-1.5">
					<span className="w-1.5 h-1.5 rounded-full" style={{ background: dotColor }} />
					<span className="font-medium">{label}</span>
				</span>

				<span className="flex-1" />

				<button
					type="button"
					onClick={() => setOpen(!contextOpen)}
					title="Context window"
					className="flex items-center gap-1.5 h-6 px-2 rounded-md transition-colors hover:bg-[var(--interactive-hover)]"
					style={{ color: pctColor }}
				>
					<Icon name="pulse" className="h-3.5 w-3.5" />
					{pct !== null && (
						<span className="tabular-nums font-medium">{pct.toFixed(1)}%</span>
					)}
				</button>
			</div>
		</div>
	);
}
