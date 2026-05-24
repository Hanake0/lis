import { useRef, useEffect, useState } from 'react';
import { useChatStore, type ChatMessage } from '../store';
import { UserMessage } from './messages/UserMessage';
import { AssistantMessage } from './messages/AssistantMessage';
import { ThinkingBlock } from './messages/ThinkingBlock';
import { ToolBlock } from './messages/ToolBlock';
import { TypingIndicator } from './messages/TypingIndicator';
import { ErrorMessage } from './messages/ErrorMessage';
import { ChatEmptyState } from './ChatEmptyState';
import { Icon } from './icon/Icon';

export function MessageList() {
	const listRef   = useRef<HTMLDivElement>(null);
	const [showJump, setShowJump] = useState(false);
	const msgs   = useChatStore(s => s.messages);
	const typing = useChatStore(s => s.isTyping);

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

	const isEmpty = msgs.length === 0 && !typing;

	return (
		<div className="flex-1 relative overflow-hidden">
			<div
				ref={listRef}
				onScroll={onScroll}
				className="absolute inset-0 overflow-y-auto"
			>
				{isEmpty ? (
					<ChatEmptyState />
				) : (
					<div className="py-6">
						{msgs.map(m => <MessageRow key={m.id} msg={m} />)}
						{typing && <TypingIndicator />}
						<div className="h-4" />
					</div>
				)}
			</div>
			{showJump && (
				<button
					type="button"
					onClick={jumpToBottom}
					title="Scroll to bottom"
					className="absolute bottom-3 right-3 flex items-center justify-center w-9 h-9 rounded-full transition-colors hover:bg-[var(--interactive-hover)]"
					style={{
						background: 'var(--surface-elevated)',
						color:      'var(--foreground)',
						border:     '1px solid var(--border)',
						boxShadow:  '0 4px 12px rgba(0,0,0,0.18)',
					}}
				>
					<Icon name="arrow-down-s" className="w-4 h-4" />
				</button>
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
