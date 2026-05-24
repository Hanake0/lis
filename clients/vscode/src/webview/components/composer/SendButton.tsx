import { RiSendPlaneFill } from '@remixicon/react';

interface Props {
	disabled: boolean;
	onClick: () => void;
}

export function SendButton({ disabled, onClick }: Props) {
	return (
		<button
			type="button"
			disabled={disabled}
			onClick={onClick}
			title="Send (Enter)"
			aria-label="Send"
			className="flex items-center justify-center w-7 h-7 rounded-md transition-opacity disabled:opacity-30"
			style={{ background: disabled ? 'transparent' : 'var(--primary)', color: disabled ? 'var(--muted-foreground)' : 'var(--primary-foreground)' }}
		>
			<RiSendPlaneFill className="w-3.5 h-3.5" />
		</button>
	);
}
