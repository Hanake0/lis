import { useChatStore } from '../../store';
import { Icon } from '../icon/Icon';

export function MessageHeader() {
	const agent = useChatStore(s => s.currentAgent);
	if (!agent) return null;

	const model   = agent.model;
	const effort  = agent.effort;
	const profile = agent.tool_profile;

	return (
		<div className="chat-column flex items-center gap-2 mt-3 mb-1 typography-meta" style={{ color: 'var(--muted-foreground)' }}>
			<span
				className="inline-flex items-center gap-1.5 px-2 py-0.5 rounded-md font-medium"
				style={{ background: 'var(--surface-subtle)', border: '1px solid var(--border)', color: 'var(--foreground)' }}
			>
				<Icon name="settings-3" className="w-3.5 h-3.5 opacity-70" />
				<span>{model}</span>
			</span>
			{profile && (
				<span className="inline-flex items-center gap-1">
					<span className="w-1.5 h-1.5 rounded-full" style={{ background: 'var(--status-success)' }} />
					<span>{profile}</span>
				</span>
			)}
			{effort && (
				<span className="inline-flex items-center gap-1">
					<span className="w-1.5 h-1.5 rounded-full" style={{ background: 'var(--status-info)' }} />
					<span>{effort}</span>
				</span>
			)}
		</div>
	);
}
