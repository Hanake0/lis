import { useRef, useEffect, useMemo } from 'preact/hooks';
import { renderMarkdown } from '../../markdownRenderer';
import { TokenBadge } from './TokenBadge';
import type { ChatMessage } from '../../state';

type AssistantMsg = Extract<ChatMessage, { kind: 'assistant' }>;

export function AssistantMessage({ msg }: { msg: AssistantMsg }) {
	const bodyRef = useRef<HTMLDivElement>(null);

	// Copy button handler via event delegation
	useEffect(() => {
		const el = bodyRef.current;
		if (!el) return;
		function onCopy(e: MouseEvent) {
			const btn = (e.target as HTMLElement).closest('.copy-btn') as HTMLElement | null;
			if (!btn) return;
			e.stopPropagation();
			const code = decodeURIComponent(btn.dataset.code ?? '');
			navigator.clipboard.writeText(code).then(() => {
				btn.textContent = '✓';
				setTimeout(() => { btn.textContent = 'Copy'; }, 1500);
			}).catch(() => undefined);
		}
		el.addEventListener('click', onCopy);
		return () => el.removeEventListener('click', onCopy);
	}, []);

	const html = useMemo(() => renderMarkdown(msg.text), [msg.text]);

	return (
		<div class={`msg msg--assistant${msg.streaming ? ' msg--streaming' : ''}`}>
			<div
				class="msg-content prose"
				ref={bodyRef}
				// eslint-disable-next-line react/no-danger
				dangerouslySetInnerHTML={{ __html: html }}
			/>
			{!msg.streaming && msg.tokens && <TokenBadge tokens={msg.tokens} />}
		</div>
	);
}
