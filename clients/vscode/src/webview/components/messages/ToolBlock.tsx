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
		? <i class="codicon codicon-loading codicon-modifier-spin tool-spinner" aria-label="Running" />
		: msg.status === 'done'
		? <i class="codicon codicon-check tool-done-icon" aria-label="Done" />
		: <i class="codicon codicon-close tool-error-icon" aria-label="Error" />;

	return (
		<div class={`tool-block ${statusClass}`}>
			<button type="button" class="tool-header" onClick={() => setCollapsed(c => !c)}>
				<i class="codicon codicon-gear tool-icon-codicon" aria-hidden="true" />
				<span class="tool-name">{formatName(msg.name)}</span>
				<span class="tool-spacer" />
				{msg.tokens !== undefined && <span class="tool-tokens">{formatTok(msg.tokens)}</span>}
				{statusIcon}
				<i class={`codicon ${collapsed ? 'codicon-chevron-right' : 'codicon-chevron-down'} tool-chevron`} />
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
