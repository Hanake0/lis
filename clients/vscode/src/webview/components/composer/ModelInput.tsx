import { Chip } from './Chip';
import { composerModel, currentAgent, agents } from '../../state';

export function ModelInput() {
	const val   = composerModel.value;
	const agent = currentAgent.value;
	const dirty = val !== (agent?.model ?? '');
	const label = val || 'model';

	const recents = [...new Set(agents.value.map(a => a.model))];

	return (
		<Chip label={label} dirty={dirty}>
			<div class="model-panel">
				<input
					type="text"
					class="model-input"
					value={val}
					onInput={e => { composerModel.value = (e.target as HTMLInputElement).value; }}
					placeholder="Model override…"
					// eslint-disable-next-line jsx-a11y/no-autofocus
					autofocus
				/>
				{recents.length > 0 && (
					<div class="model-recents">
						{recents.map(m => (
							<button
								key={m}
								type="button"
								class={`model-recent${val === m ? ' model-recent--active' : ''}`}
								onClick={() => { composerModel.value = m; }}
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
