interface Props {
	value: string;
	onChange: (v: string) => void;
	onSend: () => void;
}

export function MessageInput({ value, onChange, onSend }: Props) {
	function onKey(e: KeyboardEvent) {
		if (e.key === 'Enter' && !e.shiftKey) {
			e.preventDefault();
			onSend();
		}
	}

	function onInput(e: Event) {
		const el = e.target as HTMLTextAreaElement;
		onChange(el.value);
		el.style.height = 'auto';
		el.style.height = `${Math.min(200, el.scrollHeight)}px`;
	}

	return (
		<textarea
			class="composer-textarea"
			value={value}
			rows={1}
			placeholder="Message Lis (Enter to send, Shift+Enter for newline)"
			onKeyDown={onKey}
			onInput={onInput}
			// eslint-disable-next-line jsx-a11y/no-autofocus
			autofocus
		/>
	);
}
