import { useState } from 'preact/hooks';
import type { ChatMessage } from '../../state';

type ThinkingMsg = Extract<ChatMessage, { kind: 'thinking' }>;

export function ThinkingBlock({ msg }: { msg: ThinkingMsg }) {
	const [collapsed, setCollapsed] = useState(msg.collapsed);

	const noContent = !msg.content || msg.content.length === 0;
	const bodyText  = noContent && !msg.streaming ? '(thinking content not retained)' : (msg.content ?? '');

	return (
		<div class={`thinking-block${msg.streaming ? ' thinking-block--streaming' : ''}${collapsed ? ' thinking-block--collapsed' : ''}`}>
			<button
				type="button"
				class="thinking-header"
				onClick={() => setCollapsed(c => !c)}
			>
				<span class="thinking-chevron">{collapsed ? '▶' : '▼'}</span>
				<span class="thinking-label">{msg.streaming ? 'Thinking' : 'Thought'}</span>
				{msg.tokens !== undefined && (
					<span class="thinking-tokens">{formatTok(msg.tokens)}</span>
				)}
			</button>
			{!collapsed && (
				<div class="thinking-body">{bodyText}</div>
			)}
		</div>
	);
}

function formatTok(n: number): string {
	if (n >= 1_000_000) return `${(n / 1_000_000).toFixed(1)}M`;
	if (n >= 1_000)     return `${(n / 1_000).toFixed(n >= 10_000 ? 0 : 1)}k`;
	return `${n}`;
}
