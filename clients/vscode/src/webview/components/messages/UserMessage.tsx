import type { ChatMessage } from '../../state';

type UserMsg = Extract<ChatMessage, { kind: 'user' }>;

export function UserMessage({ msg }: { msg: UserMsg }) {
	return (
		<div class="msg msg--user">
			<div class="msg-bubble msg-bubble--user">{msg.text}</div>
		</div>
	);
}
