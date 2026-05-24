import { useRef, useEffect, useState } from 'preact/hooks';
import { messages, isTyping, type ChatMessage } from '../state';
import { UserMessage } from './messages/UserMessage';
import { AssistantMessage } from './messages/AssistantMessage';
import { ThinkingBlock } from './messages/ThinkingBlock';
import { ToolBlock } from './messages/ToolBlock';
import { TypingIndicator } from './messages/TypingIndicator';
import { ErrorMessage } from './messages/ErrorMessage';

export function MessageList() {
	const listRef    = useRef<HTMLDivElement>(null);
	const [showJump, setShowJump] = useState(false);
	const msgs    = messages.value;
	const typing  = isTyping.value;

	// Auto-scroll when messages change if user is near bottom
	useEffect(() => {
		const el = listRef.current;
		if (!el) return;
		const nearBottom = el.scrollHeight - el.scrollTop - el.clientHeight < 150;
		if (nearBottom) el.scrollTop = el.scrollHeight;
	}, [msgs.length, typing]);

	function onScroll() {
		const el = listRef.current;
		if (!el) return;
		const distFromBottom = el.scrollHeight - el.scrollTop - el.clientHeight;
		setShowJump(distFromBottom > 150);
	}

	function jumpToBottom() {
		const el = listRef.current;
		if (el) el.scrollTop = el.scrollHeight;
		setShowJump(false);
	}

	return (
		<div class="message-list" ref={listRef} onScroll={onScroll}>
			{msgs.map(m => <MessageRow key={m.id} msg={m} />)}
			{typing && <TypingIndicator />}
			{showJump && (
				<button class="jump-btn" type="button" onClick={jumpToBottom} title="Scroll to bottom">↓</button>
			)}
		</div>
	);
}

function MessageRow({ msg }: { msg: ChatMessage }) {
	switch (msg.kind) {
		case 'user':      return <UserMessage msg={msg} />;
		case 'assistant': return <AssistantMessage msg={msg} />;
		case 'thinking':  return <ThinkingBlock msg={msg} />;
		case 'tool':      return <ToolBlock msg={msg} />;
		case 'error':     return <ErrorMessage text={msg.text} />;
	}
}
