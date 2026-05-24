import { useState, useEffect, useRef, useLayoutEffect } from 'react';
import { animate, type AnimationPlaybackControls } from 'motion';
import { cn } from '../../lib/utils';
import { Icon } from '../icon/Icon';
import type { IconName } from '../icon/icons';
import { useDurationTickerNow } from './useDurationTicker';
import type { ChatMessage } from '../../store';

type ToolMsg = Extract<ChatMessage, { kind: 'tool' }>;

const EXPANDED_CONTENT_SPRING = { type: 'spring' as const, visualDuration: 0.35, bounce: 0 };

function toolIconName(name: string): string {
	const n = name.toLowerCase();
	if (n.includes('bash') || n.includes('shell') || n.includes('terminal')) return 'terminal-box';
	if (n.includes('edit') || n.includes('write') || n.includes('patch'))    return 'code';
	if (n.includes('read'))                                                   return 'book-open';
	if (n.includes('glob') || n.includes('find'))                             return 'search';
	if (n.includes('grep'))                                                   return 'search';
	if (n.includes('git'))                                                    return 'git-branch';
	if (n.includes('file'))                                                   return 'file';
	if (n.includes('folder') || n.includes('dir'))                            return 'folder';
	if (n.includes('time') || n.includes('date'))                             return 'time';
	if (n.includes('think'))                                                  return 'brain-ai-3';
	return 'tools';
}

function formatName(name: string): string {
	const parts = name.split('_');
	if (parts.length >= 2) return `${parts[0]}:${parts.slice(1).join('_')}`;
	return name;
}

function formatTok(n: number): string {
	if (n >= 1_000_000) return `${(n / 1_000_000).toFixed(1)}M`;
	if (n >= 1_000)     return `${(n / 1_000).toFixed(n >= 10_000 ? 0 : 1)}k`;
	return `${n}`;
}

function formatDur(ms: number): string {
	if (ms < 1_000) return `${ms}ms`;
	const s = ms / 1_000;
	if (s < 60) return `${s.toFixed(1)}s`;
	const m = Math.floor(s / 60);
	const rs = Math.round(s % 60);
	return `${m}m${rs}s`;
}

function escHtml(s: string): string {
	return s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}

function jsonHighlight(raw: string): string {
	let formatted = raw;
	try {
		formatted = JSON.stringify(JSON.parse(raw), null, 2);
	} catch { /* not JSON, leave as-is */ }

	return escHtml(formatted).replace(
		/("(?:\\u[a-fA-F0-9]{4}|\\[^u]|[^\\"])*"(?:\s*:)?|true|false|null|-?\d+(?:\.\d*)?(?:[eE][+\-]?\d+)?)/g,
		(match) => {
			if (/^"/.test(match)) {
				return match.endsWith(':')
					? `<span class="jk">${match}</span>`
					: `<span class="js">${match}</span>`;
			}
			if (match === 'true' || match === 'false') return `<span class="jb">${match}</span>`;
			if (match === 'null')                      return `<span class="jn">${match}</span>`;
			return `<span class="jd">${match}</span>`;
		},
	);
}

function tryParseArgs(raw: string | undefined): string | undefined {
	if (!raw) return undefined;
	const trimmed = raw.trim();
	if (!trimmed || trimmed === '{}' || trimmed === 'null') return undefined;
	return raw;
}

function summarizeArgs(raw: string | undefined): string {
	const cleaned = tryParseArgs(raw);
	if (!cleaned) return '';
	try {
		const parsed = JSON.parse(cleaned);
		if (parsed && typeof parsed === 'object' && !Array.isArray(parsed)) {
			const entries = Object.entries(parsed)
				.map(([k, v]) => {
					if (typeof v === 'string')                          return `${k}: ${v.length > 40 ? `${v.slice(0, 40)}…` : v}`;
					if (typeof v === 'number' || typeof v === 'boolean') return `${k}: ${v}`;
					return `${k}: …`;
				});
			return entries.slice(0, 3).join(' · ');
		}
		if (typeof parsed === 'string') return parsed.length > 80 ? `${parsed.slice(0, 80)}…` : parsed;
	} catch { /* fall through */ }
	const flat = cleaned.replace(/\s+/g, ' ').trim();
	return flat.length > 80 ? `${flat.slice(0, 80)}…` : flat;
}

export function ToolBlock({ msg }: { msg: ToolMsg }) {
	const isRunning = msg.status === 'running';
	const isError   = msg.status === 'error';

	const [isExpanded, setIsExpanded] = useState(isRunning);
	const contentRef     = useRef<HTMLDivElement>(null);
	const animationRef   = useRef<AnimationPlaybackControls | null>(null);
	const mountedRef     = useRef(false);
	const startTimeRef   = useRef<number>(Date.now());
	const finalDurRef    = useRef<number | null>(null);
	const prevRunningRef = useRef(isRunning);

	const now = useDurationTickerNow(isRunning, 250);
	const elapsed = isRunning
		? now - startTimeRef.current
		: (finalDurRef.current ?? 0);

	useEffect(() => {
		if (prevRunningRef.current && !isRunning) {
			finalDurRef.current = Date.now() - startTimeRef.current;
		}
		prevRunningRef.current = isRunning;
	}, [isRunning]);

	useEffect(() => { if (!isRunning) setIsExpanded(false); }, [isRunning]);

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

	const cleanedArgs    = tryParseArgs(msg.args);
	const argsSummary    = !isExpanded ? summarizeArgs(msg.args) : '';
	const hasResult      = msg.result !== undefined || msg.error !== undefined;
	const titleStyle     = { color: isError ? 'var(--status-error)' : 'var(--tools-title)' };
	const iconName       = toolIconName(msg.name);

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
				<div className="flex gap-1.5 items-center flex-shrink-0">
					<div className="relative h-3.5 w-3.5 flex-shrink-0">
						<div
							className={cn(
								'absolute inset-0 transition-opacity',
								isExpanded && 'opacity-0',
								!isExpanded && 'group-hover/tool:opacity-0',
							)}
							style={{ color: isError ? 'var(--status-error)' : 'var(--tools-icon)' }}
						>
							{isRunning
								? <Icon name="loader" className="h-3.5 w-3.5" style={{ animation: 'oc-busy-pulse 1.2s ease-in-out infinite' }} />
								: isError
								? <Icon name="close-circle" className="h-3.5 w-3.5" />
								: <Icon name={iconName as IconName} className="h-3.5 w-3.5" />}
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

					<span
						className="typography-meta font-medium flex-shrink-0"
						style={titleStyle}
						title={msg.name}
					>
						{formatName(msg.name)}
					</span>
				</div>

				<div className="flex items-center gap-1 flex-1 min-w-0 typography-meta" style={{ color: 'var(--tools-description)' }}>
					{argsSummary && (
						<span
							className="min-w-0 truncate typography-meta"
							style={{ color: 'var(--tools-description)', opacity: 0.8 }}
							title={argsSummary}
						>
							{argsSummary}
						</span>
					)}
				</div>

				<div className="flex items-center gap-2 flex-shrink-0">
					{(isRunning || (finalDurRef.current !== null && finalDurRef.current >= 500)) && (
						<span className="typography-meta tabular-nums" style={{ color: 'var(--tools-description)' }}>
							{formatDur(elapsed)}
						</span>
					)}
					{msg.tokens !== undefined && msg.tokens > 0 && (
						<span className="typography-meta tabular-nums" style={{ color: 'var(--tools-description)' }}>
							{formatTok(msg.tokens)}
						</span>
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
						style={{ backgroundColor: isError ? 'var(--status-error)' : 'var(--tools-border)' }}
					/>
					<div className="space-y-2 overflow-y-auto max-h-96">
						{cleanedArgs && (
							<section className="space-y-0.5">
								<div className="typography-meta font-medium" style={{ color: 'var(--tools-description)', opacity: 0.7 }}>
									Args
								</div>
								<pre
									className="typography-code whitespace-pre-wrap break-words"
									style={{ color: 'var(--foreground)' }}
									dangerouslySetInnerHTML={{ __html: jsonHighlight(cleanedArgs) }}
								/>
							</section>
						)}
						{hasResult && (
							<section className="space-y-0.5">
								<div
									className="typography-meta font-medium"
									style={{ color: isError ? 'var(--status-error)' : 'var(--tools-description)', opacity: isError ? 1 : 0.7 }}
								>
									{isError ? 'Error' : 'Result'}
								</div>
								<pre
									className="typography-code whitespace-pre-wrap break-words"
									style={{ color: isError ? 'var(--status-error)' : 'var(--foreground)' }}
									dangerouslySetInnerHTML={{ __html: jsonHighlight(msg.error ?? msg.result ?? '') }}
								/>
							</section>
						)}
					</div>
				</div>
			</div>
		</div>
	);
}
