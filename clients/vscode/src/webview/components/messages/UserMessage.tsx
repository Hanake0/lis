import type { ChatMessage } from '../../store';

type UserMsg = Extract<ChatMessage, { kind: 'user' }>;

export function UserMessage({ msg }: { msg: UserMsg }) {
	return (
		<div className="chat-column my-3">
			<div className="flex justify-end">
				<div
					className="max-w-[82%] px-4 py-2.5 rounded-2xl typography-markdown break-words whitespace-pre-wrap"
					style={{
						background: 'var(--secondary)',
						color:      'var(--secondary-foreground)',
						border:     '1px solid var(--border)',
					}}
				>
					{msg.text}
				</div>
			</div>
		</div>
	);
}
