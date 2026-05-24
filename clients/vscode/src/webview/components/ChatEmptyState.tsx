import { Icon } from './icon/Icon';

export function ChatEmptyState() {
	return (
		<div className="h-full flex flex-col items-center justify-center px-6 py-12 text-center">
			<div
				className="flex items-center justify-center w-14 h-14 rounded-2xl mb-4"
				style={{ background: 'var(--surface-elevated)', border: '1px solid var(--border)' }}
			>
				<Icon name="sparkling" className="w-7 h-7" style={{ color: 'var(--primary)' }} />
			</div>
			<div className="typography-ui-header font-semibold mb-1" style={{ color: 'var(--foreground)' }}>
				How can I help?
			</div>
			<div className="typography-ui-label max-w-sm" style={{ color: 'var(--muted-foreground)' }}>
				Ask anything, run a tool, or pick an agent from the composer below.
			</div>
		</div>
	);
}
