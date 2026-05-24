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
			{/* Up-arrow icon */}
			<svg width="14" height="14" viewBox="0 0 14 14" fill="currentColor" aria-hidden="true">
				<path d="M7 1L13 7H9V13H5V7H1L7 1Z" />
			</svg>
		</button>
	);
}
