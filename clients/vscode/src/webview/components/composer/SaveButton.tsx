import { useState } from 'react';
import { RiSave3Line, RiCheckLine } from '@remixicon/react';

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
			onClick={handle}
			disabled={!dirty && !saved}
			title="Save overrides to agent"
			aria-label="Save overrides"
			className="flex items-center justify-center w-7 h-7 rounded-md transition-colors disabled:opacity-30"
			style={{ color: saved ? 'var(--status-success)' : dirty ? 'var(--primary)' : 'var(--muted-foreground)' }}
		>
			{saved ? <RiCheckLine className="w-3.5 h-3.5" /> : <RiSave3Line className="w-3.5 h-3.5" />}
		</button>
	);
}
