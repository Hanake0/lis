import { Chip } from './Chip';
import { composerThinking } from '../../state';

const OPTIONS = ['budget', 'adaptive'];

export function ThinkingModePicker() {
	const val   = composerThinking.value;
	const label = val || 'thinking';

	return (
		<Chip label={label}>
			<div class="pill-header">Thinking mode</div>
			<div class="pill-group">
				<button
					type="button"
					class={`pill${val === '' ? ' pill--active' : ''}`}
					onClick={() => { composerThinking.value = ''; }}
				>
					auto
				</button>
				{OPTIONS.map(opt => (
					<button
						key={opt}
						type="button"
						class={`pill${val === opt ? ' pill--active' : ''}`}
						onClick={() => { composerThinking.value = opt; }}
					>
						{opt}
					</button>
				))}
			</div>
		</Chip>
	);
}
