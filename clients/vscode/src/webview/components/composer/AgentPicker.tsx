import { Chip } from './Chip';
import { agents, currentAgent, chatId } from '../../state';
import { vscode } from '../../vscodeApi';

export function AgentPicker() {
	const list   = agents.value;
	const active = currentAgent.value;
	const label  = active?.display_name ?? active?.name ?? 'Agent';

	function select(name: string) {
		const id = chatId.value;
		if (!id) return;
		vscode.postMessage({ type: 'send_frame', frame: { type: 'set_agent', chat_id: id, agent_name: name } });
	}

	return (
		<Chip label={label}>
			<div class="agent-list">
				{list.map(a => (
					<button
						key={a.name}
						type="button"
						class={`agent-card${a.name === active?.name ? ' agent-card--active' : ''}`}
						onClick={() => select(a.name)}
					>
						<div class="agent-card-row">
							<span class="agent-name">{a.display_name ?? a.name}</span>
							{a.is_default && <span class="agent-default">default</span>}
						</div>
						<div class="agent-meta">
							{a.model}{a.tool_profile ? ` · ${a.tool_profile}` : ''}{a.effort ? ` · ${a.effort}` : ''}
						</div>
					</button>
				))}
			</div>
		</Chip>
	);
}
