import { Chip } from './Chip';
import { cn } from '../../lib/utils';
import { useChatStore } from '../../store';
import { vscode } from '../../vscodeApi';

export function AgentPicker() {
	const list   = useChatStore(s => s.agents);
	const active = useChatStore(s => s.currentAgent);
	const chatId = useChatStore(s => s.chatId);
	const label  = active?.display_name ?? active?.name ?? 'Agent';

	function select(name: string) {
		if (!chatId) return;
		vscode.postMessage({ type: 'send_frame', frame: { type: 'set_agent', chat_id: chatId, agent_name: name } });
	}

	return (
		<Chip label={label}>
			<div className="flex flex-col gap-0.5 max-h-72 overflow-y-auto">
				{list.map(a => (
					<button
						key={a.name}
						type="button"
						onClick={() => select(a.name)}
						className={cn(
							'flex flex-col gap-0.5 w-full px-2 py-1.5 text-left rounded-md transition-colors',
							'hover:bg-[var(--interactive-hover)]',
							a.name === active?.name && 'bg-[var(--interactive-active)]',
						)}
					>
						<div className="flex items-center justify-between gap-2">
							<span className="typography-ui-label font-medium" style={{ color: 'var(--foreground)' }}>{a.display_name ?? a.name}</span>
							{a.is_default && (
								<span className="typography-meta px-1 rounded" style={{ background: 'var(--surface-subtle)', color: 'var(--muted-foreground)' }}>
									default
								</span>
							)}
						</div>
						<div className="typography-meta truncate" style={{ color: 'var(--muted-foreground)' }}>
							{a.model}{a.tool_profile ? ` · ${a.tool_profile}` : ''}{a.effort ? ` · ${a.effort}` : ''}
						</div>
					</button>
				))}
			</div>
		</Chip>
	);
}
