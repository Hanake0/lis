import { useState, useRef, useEffect } from 'preact/hooks';
import type { ChatMessage } from '../../state';

type ToolMsg = Extract<ChatMessage, { kind: 'tool' }>;

export function ToolBlock({ msg }: { msg: ToolMsg }) {
	const [collapsed, setCollapsed] = useState(msg.status !== 'running');
	const [maxH, setMaxH] = useState<number | undefined>(undefined);
	const [canScrollDown, setCanScrollDown] = useState(false);
	const bodyRef = useRef<HTMLDivElement>(null);
	const dragRef = useRef<{ startY: number; startH: number } | null>(null);

	function checkScroll() {
		const el = bodyRef.current;
		if (!el) return;
		setCanScrollDown(el.scrollTop + el.clientHeight < el.scrollHeight - 2);
	}

	useEffect(() => { checkScroll(); }, [collapsed, msg.result, msg.error]);

	function onDragStart(e: PointerEvent) {
		const body = bodyRef.current;
		if (!body) return;
		(e.target as HTMLElement).setPointerCapture(e.pointerId);
		dragRef.current = { startY: e.clientY, startH: body.clientHeight };
	}
	function onDragMove(e: PointerEvent) {
		if (!dragRef.current) return;
		const delta = e.clientY - dragRef.current.startY;
		setMaxH(Math.max(60, dragRef.current.startH + delta));
	}
	function onDragEnd() { dragRef.current = null; }

	const statusClass = msg.status === 'running' ? 'tool-block--running'
		: msg.status === 'error' ? 'tool-block--error'
		: 'tool-block--done';

	const statusIcon = msg.status === 'running'
		? <span class="tool-spinner" aria-label="Running" />
		: msg.status === 'done'
		? <svg class="tool-done-icon" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-label="Done"><polyline points="3 8 6.5 11.5 13 5" /></svg>
		: <svg class="tool-error-icon" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" aria-label="Error"><line x1="4" y1="4" x2="12" y2="12"/><line x1="12" y1="4" x2="4" y2="12"/></svg>;

	return (
		<div class={`tool-block ${statusClass}`}>
			<button type="button" class="tool-header" onClick={() => setCollapsed(c => !c)}>
				<svg class="tool-icon-svg" viewBox="0 0 16 16" fill="currentColor" aria-hidden="true">
					<path d="M9.1 1.7a1 1 0 0 0-2.2 0l-.2.9a5 5 0 0 0-.9.5l-.9-.3a1 1 0 0 0-1.1.5L2.7 4.5a1 1 0 0 0 .2 1.2l.7.6v1l-.7.6a1 1 0 0 0-.2 1.2l1.1 1.2a1 1 0 0 0 1.1.5l.9-.3c.3.2.6.4.9.5l.2.9a1 1 0 0 0 2.2 0l.2-.9c.3-.1.6-.3.9-.5l.9.3a1 1 0 0 0 1.1-.5l1.1-1.2a1 1 0 0 0-.2-1.2l-.7-.6v-1l.7-.6a1 1 0 0 0 .2-1.2L12.1 3.3a1 1 0 0 0-1.1-.5l-.9.3a5 5 0 0 0-.9-.5l-.1-.9zM8 6a2 2 0 1 1 0 4A2 2 0 0 1 8 6z"/>
				</svg>
				<span class="tool-name">{formatName(msg.name)}</span>
				<span class="tool-spacer" />
				{msg.tokens !== undefined && <span class="tool-tokens">{formatTok(msg.tokens)}</span>}
				{statusIcon}
				<span class="tool-chevron">{collapsed ? '▶' : '▼'}</span>
			</button>

			{!collapsed && (
				<div class="tool-body-wrap">
					<div class="tool-body" ref={bodyRef} onScroll={checkScroll} style={maxH !== undefined ? { maxHeight: `${maxH}px` } : undefined}>
						{msg.args && msg.args !== '{}' && (
							<section class="tool-section">
								<div class="tool-section-label">Args</div>
								<pre class="tool-code" dangerouslySetInnerHTML={{ __html: jsonHighlight(msg.args) }} />
							</section>
						)}
						{(msg.result !== undefined || msg.error !== undefined) && (
							<section class={`tool-section${msg.error ? ' tool-section--error' : ''}`}>
								<div class="tool-section-label">{msg.error ? 'Error' : 'Result'}</div>
								<pre class="tool-code tool-result" dangerouslySetInnerHTML={{ __html: jsonHighlight(msg.error ?? msg.result ?? '') }} />
							</section>
						)}
					</div>
					{canScrollDown && <div class="tool-fade-bot" />}
					<div
						class="tool-resize-handle"
						onPointerDown={onDragStart}
						onPointerMove={onDragMove}
						onPointerUp={onDragEnd}
						title="Drag to resize"
					/>
				</div>
			)}
		</div>
	);
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

function jsonHighlight(raw: string): string {
	let formatted = raw;
	try {
		const parsed = JSON.parse(raw);
		formatted = JSON.stringify(parsed, null, 2);
	} catch { /* not JSON */ }

	return escHtml(formatted).replace(
		/("(?:\\u[a-fA-F0-9]{4}|\\[^u]|[^\\"])*"(?:\s*:)?|true|false|null|-?\d+(?:\.\d*)?(?:[eE][+\-]?\d+)?)/g,
		match => {
			if (/^"/.test(match)) {
				return match.endsWith(':')
					? `<span class="jk">${match}</span>`
					: `<span class="js">${match}</span>`;
			}
			if (match === 'true' || match === 'false') return `<span class="jb">${match}</span>`;
			if (match === 'null')                       return `<span class="jn">${match}</span>`;
			return `<span class="jd">${match}</span>`;
		},
	);
}

function escHtml(s: string): string {
	return s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}
