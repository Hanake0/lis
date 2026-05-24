import { Chip } from './Chip';
import { composerProfile, currentAgent } from '../../state';

const OPTIONS = ['minimal', 'standard', 'coding', 'full'];

export function ProfilePicker() {
	const val   = composerProfile.value;
	const agent = currentAgent.value;
	const dirty = val !== (agent?.tool_profile ?? '');
	const label = val || 'profile';

	return (
		<Chip label={label} dirty={dirty}>
			<div class="pill-header">Tool profile</div>
			<div class="pill-group">
				<button
					type="button"
					class={`pill${val === '' ? ' pill--active' : ''}`}
					onClick={() => { composerProfile.value = ''; }}
				>
					auto
				</button>
				{OPTIONS.map(opt => (
					<button
						key={opt}
						type="button"
						class={`pill${val === opt ? ' pill--active' : ''}`}
						onClick={() => { composerProfile.value = opt; }}
					>
						{opt}
					</button>
				))}
			</div>
		</Chip>
	);
}
