import { Chip } from './Chip';
import { cn } from '../../lib/utils';
import { useChatStore } from '../../store';

export function ModelInput() {
	const val      = useChatStore(s => s.composerModel);
	const setVal   = useChatStore(s => s.setComposerModel);
	const agent    = useChatStore(s => s.currentAgent);
	const agents   = useChatStore(s => s.agents);
	const dirty    = val !== (agent?.model ?? '');
	const label    = val || 'model';
	const recents  = [...new Set(agents.map(a => a.model))];

	return (
		<Chip label={label} dirty={dirty}>
			<div className="flex flex-col gap-1.5 min-w-[18rem]">
				<input
					type="text"
					value={val}
					onChange={(e) => setVal(e.currentTarget.value)}
					placeholder="Model override…"
					autoFocus
					className="px-2 py-1 rounded-md typography-ui-label outline-none"
					style={{
						background: 'var(--input)',
						color:      'var(--foreground)',
						border:     '1px solid var(--border)',
					}}
				/>
				{recents.length > 0 && (
					<div className="flex flex-col gap-0.5">
						{recents.map(m => (
							<button
								key={m}
								type="button"
								onClick={() => setVal(m)}
								className={cn(
									'px-2 py-1 text-left rounded-md typography-meta transition-colors truncate',
									'hover:bg-[var(--interactive-hover)]',
									val === m && 'bg-[var(--interactive-active)]',
								)}
								style={{ color: 'var(--foreground)' }}
							>
								{m}
							</button>
						))}
					</div>
				)}
			</div>
		</Chip>
	);
}
