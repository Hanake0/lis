import React, { useCallback } from 'react';
import { useSidebarStore } from './sidebarStore';
import { cn } from '@/lib/utils';

const vscode = () => (window as any).__vscode;

const formatTimeAgo = (dateStr?: string): string => {
	if (!dateStr) return '';
	const diff = Date.now() - new Date(dateStr).getTime();
	const mins = Math.floor(diff / 60000);
	if (mins < 1) return 'now';
	if (mins < 60) return `${mins}m`;
	const hours = Math.floor(mins / 60);
	if (hours < 24) return `${hours}h`;
	const days = Math.floor(hours / 24);
	return `${days}d`;
};

const SessionItem: React.FC<{
	session: { chat_id: string; name?: string; updated_at?: string; archived_at?: string };
	isActive: boolean;
	onSelect: (id: string, title?: string) => void;
	onDelete: (id: string) => void;
	onRename: (id: string, name: string) => void;
}> = React.memo(({ session, isActive, onSelect, onDelete, onRename }) => {
	const [editing, setEditing] = React.useState(false);
	const [editValue, setEditValue] = React.useState(session.name || '');
	const inputRef = React.useRef<HTMLInputElement>(null);

	const handleDoubleClick = useCallback(() => {
		setEditValue(session.name || '');
		setEditing(true);
		requestAnimationFrame(() => inputRef.current?.select());
	}, [session.name]);

	const commitRename = useCallback(() => {
		const trimmed = editValue.trim();
		if (trimmed && trimmed !== session.name) {
			onRename(session.chat_id, trimmed);
		}
		setEditing(false);
	}, [editValue, session.chat_id, session.name, onRename]);

	return (
		<div
			className={cn(
				'group flex items-center gap-2 px-3 py-1.5 cursor-pointer rounded-md text-sm',
				'hover:bg-accent/50 transition-colors',
				isActive && 'bg-accent text-accent-foreground',
				session.archived_at && 'opacity-50',
			)}
			onClick={() => onSelect(session.chat_id, session.name)}
			onDoubleClick={handleDoubleClick}
		>
			{editing ? (
				<input
					ref={inputRef}
					className="flex-1 bg-transparent border border-border rounded px-1 py-0 text-sm text-foreground outline-none"
					value={editValue}
					onChange={(e) => setEditValue(e.target.value)}
					onBlur={commitRename}
					onKeyDown={(e) => {
						if (e.key === 'Enter') commitRename();
						if (e.key === 'Escape') setEditing(false);
					}}
					onClick={(e) => e.stopPropagation()}
				/>
			) : (
				<span className="flex-1 truncate">{session.name || 'Untitled'}</span>
			)}
			<span className="text-xs text-muted-foreground shrink-0">
				{formatTimeAgo(session.updated_at)}
			</span>
			<button
				className="opacity-0 group-hover:opacity-100 text-muted-foreground hover:text-destructive transition-opacity shrink-0 p-0.5"
				onClick={(e) => { e.stopPropagation(); onDelete(session.chat_id); }}
				title="Delete session"
			>
				<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M3 6h18M19 6v14a2 2 0 01-2 2H7a2 2 0 01-2-2V6m3 0V4a2 2 0 012-2h4a2 2 0 012 2v2"/></svg>
			</button>
		</div>
	);
});

export const SidebarApp: React.FC = () => {
	const connected = useSidebarStore((s) => s.connected);
	const sessions = useSidebarStore((s) => s.sessions);
	const activeSessionId = useSidebarStore((s) => s.activeSessionId);

	const activeSessions = React.useMemo(
		() => sessions.filter(s => !s.archived_at),
		[sessions],
	);

	const handleSelect = useCallback((chatId: string, title?: string) => {
		useSidebarStore.getState().setActiveSessionId(chatId);
		vscode().postMessage({ type: 'open_session', chat_id: chatId, title });
	}, []);

	const handleNewChat = useCallback(() => {
		const chatId = `web:${crypto.randomUUID()}`;
		vscode().postMessage({ type: 'create_session', chat_id: chatId, name: 'New Chat' });
	}, []);

	const handleDelete = useCallback((chatId: string) => {
		vscode().postMessage({ type: 'delete_session', chat_id: chatId });
	}, []);

	const handleRename = useCallback((chatId: string, name: string) => {
		vscode().postMessage({ type: 'rename_session', chat_id: chatId, name });
	}, []);

	return (
		<div className="flex flex-col h-full bg-sidebar text-sidebar-foreground">
			<div className="flex items-center justify-between px-3 py-2 border-b border-border/50">
				<span className="text-xs font-medium uppercase tracking-wider text-muted-foreground">Sessions</span>
				<button
					className="text-muted-foreground hover:text-foreground transition-colors p-0.5 rounded"
					onClick={handleNewChat}
					title="New Chat"
					disabled={!connected}
				>
					<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M12 5v14M5 12h14"/></svg>
				</button>
			</div>

			<div className="flex-1 overflow-y-auto py-1">
				{!connected ? (
					<div className="px-3 py-4 text-center text-sm text-muted-foreground">
						Connecting...
					</div>
				) : activeSessions.length === 0 ? (
					<div className="px-3 py-4 text-center text-sm text-muted-foreground">
						No sessions yet
					</div>
				) : (
					activeSessions.map((s) => (
						<SessionItem
							key={s.chat_id}
							session={s}
							isActive={s.chat_id === activeSessionId}
							onSelect={handleSelect}
							onDelete={handleDelete}
							onRename={handleRename}
						/>
					))
				)}
			</div>
		</div>
	);
};
