interface Props {
	disabled: boolean;
	onClick: () => void;
}

export function SendButton({ disabled, onClick }: Props) {
	return (
		<button
			type="button"
			class="send-btn"
			disabled={disabled}
			onClick={onClick}
			title="Send (Enter)"
			aria-label="Send"
		>
			<i class="codicon codicon-send" aria-hidden="true" />
		</button>
	);
}
