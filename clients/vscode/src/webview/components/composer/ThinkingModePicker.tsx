import type { ReactNode } from 'react';
import { Chip } from './Chip';
import { cn } from '../../lib/utils';
import { useChatStore } from '../../store';

const OPTIONS = ['budget', 'adaptive'];

export function ThinkingModePicker() {
	const val    = useChatStore(s => s.composerThinking);
	const setVal = useChatStore(s => s.setComposerThinking);
	const label  = val || 'thinking';

	return (
		<Chip label={label}>
			<div className="typography-meta px-1 pb-1" style={{ color: 'var(--muted-foreground)' }}>Thinking mode</div>
			<div className="flex flex-wrap gap-1">
				<PillButton active={val === ''} onClick={() => setVal('')}>auto</PillButton>
				{OPTIONS.map(opt => (
					<PillButton key={opt} active={val === opt} onClick={() => setVal(opt)}>{opt}</PillButton>
				))}
			</div>
		</Chip>
	);
}

function PillButton({ active, onClick, children }: { active: boolean; onClick: () => void; children: ReactNode }) {
	return (
		<button
			type="button"
			onClick={onClick}
			className={cn(
				'h-6 px-2 rounded-md typography-meta font-medium transition-colors',
				active
					? 'text-[var(--primary-foreground)]'
					: 'text-[var(--muted-foreground)] hover:text-[var(--foreground)] hover:bg-[var(--interactive-hover)]',
			)}
			style={active ? { background: 'var(--primary)' } : undefined}
		>
			{children}
		</button>
	);
}
