import { useState } from 'preact/hooks';

interface Props {
	dirty: boolean;
	onClick: () => void;
}

export function SaveButton({ dirty, onClick }: Props) {
	const [saved, setSaved] = useState(false);

	function handle() {
		if (!dirty && !saved) return;
		onClick();
		setSaved(true);
		setTimeout(() => setSaved(false), 1500);
	}

	return (
		<button
			type="button"
			class={`save-btn${dirty ? ' save-btn--dirty' : ''}${saved ? ' save-btn--saved' : ''}`}
			onClick={handle}
			disabled={!dirty && !saved}
			title="Save overrides to agent"
			aria-label="Save overrides"
		>
			{saved ? '✓' : '↓'}
		</button>
	);
}
