import { useRef, useEffect, useMemo } from 'react';
import { renderMarkdown } from '../../markdownRenderer';
import { TokenBadge } from './TokenBadge';
import { MessageActions } from './MessageActions';
import type { ChatMessage } from '../../store';

type AssistantMsg = Extract<ChatMessage, { kind: 'assistant' }>;

export function AssistantMessage({ msg }: { msg: AssistantMsg }) {
	const bodyRef = useRef<HTMLDivElement>(null);

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
		<div className="chat-column my-2 group/assistant">
			<div
				ref={bodyRef}
				className="prose"
				style={{ opacity: msg.streaming ? 0.95 : 1 }}
				dangerouslySetInnerHTML={{ __html: html }}
			/>
			{!msg.streaming && (
				<div className="flex items-center gap-1 mt-1">
					<MessageActions text={msg.text} />
					{msg.tokens && (
						<>
							<span className="flex-1" />
							<TokenBadge tokens={msg.tokens} />
						</>
					)}
				</div>
			)}
		</div>
	);
}
