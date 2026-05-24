import { useState, useRef, useEffect } from 'preact/hooks';
import type { ChatMessage } from '../../state';

type ThinkingMsg = Extract<ChatMessage, { kind: 'thinking' }>;

export function ThinkingBlock({ msg }: { msg: ThinkingMsg }) {
	const [collapsed, setCollapsed] = useState(msg.collapsed);
	const bodyRef = useRef<HTMLDivElement>(null);
	const [canScrollUp,   setCanScrollUp]   = useState(false);
	const [canScrollDown, setCanScrollDown] = useState(false);

	function checkScroll() {
		const el = bodyRef.current;
		if (!el) return;
		setCanScrollUp(el.scrollTop > 2);
		setCanScrollDown(el.scrollTop + el.clientHeight < el.scrollHeight - 2);
	}

	useEffect(() => {
		if (bodyRef.current && msg.streaming) {
			bodyRef.current.scrollTop = bodyRef.current.scrollHeight;
		}
		checkScroll();
	}, [msg.content, msg.streaming]);

	const noContent = !msg.content || msg.content.length === 0;
	const bodyText  = noContent && !msg.streaming ? '(thinking content not retained)' : (msg.content ?? '');

	return (
		<div class={`thinking-block${msg.streaming ? ' thinking-block--streaming' : ''}${collapsed ? ' thinking-block--collapsed' : ''}`}>
			<button
				type="button"
				class="thinking-header"
				onClick={() => setCollapsed(c => !c)}
			>
				<i class={`codicon ${collapsed ? 'codicon-chevron-right' : 'codicon-chevron-down'} thinking-chevron`} />
				<i class="codicon codicon-lightbulb thinking-icon" />
				<span class={`thinking-label${msg.streaming ? ' thinking-label--streaming' : ''}`}>
					{msg.streaming ? 'Thinking' : 'Thought'}
				</span>
				{msg.tokens !== undefined && (
					<span class="thinking-tokens">{formatTok(msg.tokens)}</span>
				)}
			</button>
			{!collapsed && (
				<div class="thinking-body-wrap">
					{canScrollUp   && <div class="thinking-fade thinking-fade--top" />}
					<div class="thinking-body" ref={bodyRef} onScroll={checkScroll}>{bodyText}</div>
					{canScrollDown && <div class="thinking-fade thinking-fade--bot" />}
				</div>
			)}
		</div>
	);
}

function formatTok(n: number): string {
	if (n >= 1_000_000) return `${(n / 1_000_000).toFixed(1)}M`;
	if (n >= 1_000)     return `${(n / 1_000).toFixed(n >= 10_000 ? 0 : 1)}k`;
	return `${n}`;
}
