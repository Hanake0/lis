import type { KeyboardEvent, FormEvent } from 'react';

interface Props {
	value:    string;
	onChange: (v: string) => void;
	onSend:   () => void;
}

export function MessageInput({ value, onChange, onSend }: Props) {
	function onKey(e: KeyboardEvent<HTMLTextAreaElement>) {
		if (e.key === 'Enter' && !e.shiftKey) {
			e.preventDefault();
			onSend();
		}
	}

	function onInput(e: FormEvent<HTMLTextAreaElement>) {
		const el = e.currentTarget;
		onChange(el.value);
		el.style.height = 'auto';
		el.style.height = `${Math.min(200, el.scrollHeight)}px`;
	}

	return (
		<textarea
			data-chat-input="true"
			className="block w-full resize-none bg-transparent outline-none typography-markdown px-4 py-3 leading-relaxed placeholder:opacity-50"
			style={{ color: 'var(--foreground)', minHeight: '3.25rem' }}
			value={value}
			rows={2}
			placeholder="Ask, plan, or build something…"
			onKeyDown={onKey}
			onInput={onInput}
			// eslint-disable-next-line jsx-a11y/no-autofocus
			autoFocus
		/>
	);
}
