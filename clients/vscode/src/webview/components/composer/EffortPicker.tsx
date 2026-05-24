import { Chip } from './Chip';
import { composerEffort, currentAgent } from '../../state';

const OPTIONS = ['low', 'medium', 'high', 'xhigh', 'max'];

export function EffortPicker() {
	const val   = composerEffort.value;
	const agent = currentAgent.value;
	const dirty = val !== (agent?.effort ?? '');
	const label = val || 'effort';

	return (
		<Chip label={label} dirty={dirty}>
			<div class="pill-header">Thinking effort</div>
			<div class="pill-group">
				<button
					type="button"
					class={`pill${val === '' ? ' pill--active' : ''}`}
					onClick={() => { composerEffort.value = ''; }}
				>
					auto
				</button>
				{OPTIONS.map(opt => (
					<button
						key={opt}
						type="button"
						class={`pill${val === opt ? ' pill--active' : ''}`}
						onClick={() => { composerEffort.value = opt; }}
					>
						{opt}
					</button>
				))}
			</div>
		</Chip>
	);
}
