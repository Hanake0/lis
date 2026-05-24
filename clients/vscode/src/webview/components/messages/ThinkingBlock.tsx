import { useState, useEffect, useRef, useLayoutEffect, useMemo } from 'react';
import { animate, type AnimationPlaybackControls } from 'motion';
import { cn } from '../../lib/utils';
import { Icon } from '../icon/Icon';
import { BusyDots } from './BusyDots';
import type { ChatMessage } from '../../store';

type ThinkingMsg = Extract<ChatMessage, { kind: 'thinking' }>;

const SUMMARY_MAX_CHARS = 80;
const EXPANDED_CONTENT_SPRING = { type: 'spring' as const, visualDuration: 0.35, bounce: 0 };

function stripMarkdown(text: string): string {
	return text
		.replace(/```[\w]*\n?([\s\S]*?)```/g, (_, inner: string) => inner.trim())
		.replace(/`([^`]+)`/g, '$1')
		.replace(/\*{1,3}([^*]+)\*{1,3}/g, '$1')
		.replace(/_{1,3}([^_]+)_{1,3}/g, '$1')
		.replace(/^#{1,6}\s+/gm, '')
		.replace(/\[([^\]]+)\]\([^)]*\)/g, '$1')
		.replace(/^>\s?/gm, '')
		.replace(/^[-*_]{3,}\s*$/gm, '')
		.trim();
}

function getReasoningSummary(text: string): string {
	if (!text) return '';
	const flat = stripMarkdown(text).replace(/\s+/g, ' ').trim();
	if (flat.length <= SUMMARY_MAX_CHARS) return flat;
	const cut = flat.lastIndexOf(' ', SUMMARY_MAX_CHARS);
	const end = cut > 0 ? cut : SUMMARY_MAX_CHARS;
	return `${flat.substring(0, end).trimEnd()}…`;
}

function formatTok(n: number): string {
	if (n >= 1_000_000) return `${(n / 1_000_000).toFixed(1)}M`;
	if (n >= 1_000)     return `${(n / 1_000).toFixed(n >= 10_000 ? 0 : 1)}k`;
	return `${n}`;
}

export function ThinkingBlock({ msg }: { msg: ThinkingMsg }) {
	const isStreaming = msg.streaming;
	const text        = msg.content ?? '';

	const [isExpanded, setIsExpanded] = useState(!msg.collapsed && isStreaming);
	const scrollRef        = useRef<HTMLDivElement>(null);
	const contentRef       = useRef<HTMLDivElement>(null);
	const animationRef     = useRef<AnimationPlaybackControls | null>(null);
	const mountedRef       = useRef(false);
	const prevStreamingRef = useRef(isStreaming);

	const summary = useMemo(() => getReasoningSummary(text), [text]);

	useEffect(() => {
		const wasStreaming = prevStreamingRef.current;
		prevStreamingRef.current = isStreaming;
		if (wasStreaming && !isStreaming) setIsExpanded(false);
	}, [isStreaming]);

	useEffect(() => {
		if (isStreaming && isExpanded && scrollRef.current) {
			scrollRef.current.scrollTop = scrollRef.current.scrollHeight;
		}
	}, [text, isStreaming, isExpanded]);

	useLayoutEffect(() => {
		const el = contentRef.current;
		if (!el) return;

		animationRef.current?.stop();

		if (!mountedRef.current) {
			mountedRef.current = true;
			el.style.height   = isExpanded ? 'auto'    : '0px';
			el.style.opacity  = isExpanded ? '1'       : '0';
			el.style.overflow = isExpanded ? 'visible' : 'hidden';
			return;
		}

		el.style.overflow = 'hidden';
		if (isExpanded) {
			el.style.height  = '0px';
			el.style.opacity = '0';
		} else {
			el.style.height  = `${el.scrollHeight}px`;
			el.style.opacity = '1';
		}

		const a = animate(
			el,
			{ height: isExpanded ? 'auto' : '0px', opacity: isExpanded ? 1 : 0 },
			EXPANDED_CONTENT_SPRING,
		);
		animationRef.current = a;
		void a.finished.then(() => {
			if (animationRef.current !== a) return;
			animationRef.current = null;
			if (isExpanded) {
				el.style.overflow = 'visible';
				el.style.height   = 'auto';
			} else {
				el.style.overflow = 'hidden';
			}
		}).catch(() => undefined);

		return () => { a.stop(); };
	}, [isExpanded]);

	useEffect(() => () => { animationRef.current?.stop(); }, []);

	const noContent = text.trim().length === 0;
	if (noContent && !isStreaming) {
		// Historical thinking row with no retained content: still render a hint
		return (
			<div className="chat-column my-1">
				<div className="flex items-center gap-1.5 pr-2 pl-px py-2 typography-meta" style={{ color: 'var(--muted-foreground)' }}>
					<Icon name="brain-ai-3" className="h-3.5 w-3.5" />
					<span>Thought</span>
					{msg.tokens !== undefined && (
						<span className="ml-1 tabular-nums opacity-70">{formatTok(msg.tokens)}</span>
					)}
					<span className="opacity-60 italic">(content not retained)</span>
				</div>
			</div>
		);
	}

	return (
		<div className="chat-column my-1">
			<div
				role="button"
				tabIndex={0}
				aria-expanded={isExpanded}
				className="group/tool flex gap-2 px-2 py-1.5 rounded-lg cursor-pointer items-center select-none transition-colors hover:bg-[var(--tools-header-hover)]"
				onClick={() => setIsExpanded(v => !v)}
				onKeyDown={(e) => {
					if (e.key === 'Enter' || e.key === ' ') {
						e.preventDefault();
						setIsExpanded(v => !v);
					}
				}}
			>
				<div className="flex items-center gap-1.5 flex-shrink-0">
					<div className="relative h-3.5 w-3.5 flex-shrink-0">
						<div
							className={cn(
								'absolute inset-0 transition-opacity',
								isExpanded && 'opacity-0',
								!isExpanded && 'group-hover/tool:opacity-0',
							)}
							style={{ color: 'var(--tools-icon)' }}
						>
							<Icon name="brain-ai-3" className="h-3.5 w-3.5" />
						</div>
						<div
							className={cn(
								'absolute inset-0 transition-opacity flex items-center justify-center',
								isExpanded && 'opacity-100',
								!isExpanded && 'opacity-0 group-hover/tool:opacity-100',
							)}
							style={{ color: 'var(--tools-icon)' }}
						>
							{isExpanded
								? <Icon name="arrow-down-s"  className="h-3.5 w-3.5" />
								: <Icon name="arrow-right-s" className="h-3.5 w-3.5" />}
						</div>
					</div>

					<span className="typography-meta font-medium flex items-center gap-1" style={{ color: 'var(--tools-title)' }}>
						<span>{isStreaming ? 'Thinking' : 'Thought'}</span>
						{isStreaming && <BusyDots />}
					</span>

					{msg.tokens !== undefined && msg.tokens > 0 && !isStreaming && (
						<span className="typography-meta tabular-nums opacity-60" style={{ color: 'var(--tools-description)' }}>
							{formatTok(msg.tokens)}
						</span>
					)}
				</div>

				<div className="flex items-center gap-1 flex-1 min-w-0 typography-meta" style={{ color: 'var(--tools-description)' }}>
					{!isStreaming && !isExpanded && summary ? (
						<span
							className="min-w-0 truncate typography-meta italic"
							style={{ opacity: 0.8 }}
							title={summary}
						>
							{summary}
						</span>
					) : (
						<span className="min-w-0 flex-1" />
					)}
				</div>
			</div>

			<div
				ref={contentRef}
				aria-hidden={!isExpanded}
				style={{
					height:         isExpanded ? 'auto'    : '0px',
					opacity:        isExpanded ? 1         : 0,
					overflow:       isExpanded ? 'visible' : 'hidden',
					overflowAnchor: 'none',
				}}
			>
				<div className="relative ml-2 pl-3 pb-1 pt-0.5">
					<span
						aria-hidden="true"
						className="pointer-events-none absolute left-0 top-0 bottom-0 w-px"
						style={{ backgroundColor: 'var(--tools-border)' }}
					/>
					<div
						ref={scrollRef}
						className="overflow-y-auto max-h-80 typography-meta whitespace-pre-wrap"
						style={{ color: 'var(--muted-foreground)' }}
					>
						{text}
					</div>
				</div>
			</div>
		</div>
	);
}
