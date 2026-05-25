import { SessionSidebar } from './components/session/SessionSidebar';
import { SessionDialogs } from './components/session/SessionDialogs';
import { TooltipProvider } from './components/ui/tooltip';
import { ErrorBoundary } from './components/ui/ErrorBoundary';
import { I18nProvider } from './lib/i18n';
import { vscode } from './vscodeApi';
import { useSessionUIStore } from './sync/session-ui-store';
import { useProjectsStore } from './stores/useProjectsStore';
import { normalizePath } from './components/session/sidebar/utils';
import { sessionEvents } from './lib/sessionEvents';

/**
 * Side effects required before the sidebar renders:
 *  - dark + lis-sidebar CSS scope on `<html>` so design-system tokens resolve.
 *  - Intercept `setCurrentSession` to send `open_session` to the extension host,
 *    which routes the click to `ChatPanelManager.openOrFocus`.
 *  - Intercept `openNewSessionDraft` to send `new_chat` to the extension host,
 *    which runs `lis.newChat` (creates the chat row + opens a panel). The OC
 *    store's draft state only makes sense in a single-pane desktop client; in
 *    VS Code the chat lives in its own WebviewPanel, so the sidebar can't
 *    render a draft inline.
 *
 * Project state comes from `useProjectsStore` (wired via `installProjectsSync`
 * in webview/index.ts), session lifecycle comes from `useGlobalSessionsStore`
 * (wired via `chat-events-bridge`). The legacy synthetic Lis-project hack and
 * the WS-frame postMessage handler that used to live here are both gone.
 */
export function installSidebarFrameHandler(): void {
	document.documentElement.classList.add('dark', 'lis-sidebar');

	const state    = useSessionUIStore.getState();
	const origSet  = state.setCurrentSession;
	// React StrictMode + SortableProjectItem's effect-driven "new" trigger fires
	// `openNewSessionDraft` twice in quick succession. Without a guard we POST
	// `/api/chats` twice and the user gets two stray panels. Collapse calls
	// within 500ms targeting the same project — that's well under any humanly
	// double-clickable rate.
	let lastFireKey = '';
	let lastFireAt  = 0;
	useSessionUIStore.setState({
		// OC calls setCurrentSession(id, directory) — preserve the second arg so
		// the original store state stays consistent (otherwise archived rows,
		// which always pass `null` for directory, can short-circuit downstream
		// effects that compare `(prevDir, nextDir)`).
		setCurrentSession: (id: string, directory?: string | null) => {
			vscode.postMessage({ type: 'open_session', chat_id: id, directory: directory ?? null });
			origSet(id, directory ?? null);
		},
		openNewSessionDraft: (options?: { directoryOverride?: string | null }) => {
			const dir = normalizePath(options?.directoryOverride ?? '') ?? '';
			const { projects, activeProjectId } = useProjectsStore.getState();
			const byPath  = dir ? projects.find((p) => normalizePath(p.path) === dir) : null;
			const byActive = activeProjectId ? projects.find((p) => p.id === activeProjectId) : null;
			const projectId = byPath?.id ?? byActive?.id ?? null;
			const key = projectId ?? `dir:${dir}`;
			const now = Date.now();
			if (key === lastFireKey && now - lastFireAt < 500) return;
			lastFireKey = key;
			lastFireAt  = now;
			vscode.postMessage({ type: 'new_chat', project_external_id: projectId });
		},
	});

	// Route the "+ Add project" toolbar button (and any other call site that
	// fires sessionEvents.requestDirectoryDialog) through the host so the
	// picker opens as the modal-feel editor-tab panel — not crammed inside
	// the ~485px-wide sidebar. The host command seeds the dialog with the
	// active project's path so the user lands somewhere useful.
	const origRequestDirectoryDialog = sessionEvents.requestDirectoryDialog;
	sessionEvents.requestDirectoryDialog = () => {
		const { projects, activeProjectId } = useProjectsStore.getState();
		const active = activeProjectId ? projects.find((p) => p.id === activeProjectId) : null;
		const seed   = active?.path ?? projects[0]?.path ?? null;
		vscode.postMessage({ type: 'open_add_project', default_path: seed });
		// Don't dispatch to the local listeners — the in-sidebar
		// DirectoryExplorerDialog stays closed.
		void origRequestDirectoryDialog;
	};
}

export function SidebarOCApp() {
	return (
		<I18nProvider>
			<ErrorBoundary>
				<TooltipProvider delayDuration={300} skipDelayDuration={150}>
					<div
						className="h-full w-full text-foreground dark overflow-hidden"
						style={{
							background: 'var(--vscode-sideBar-background, var(--vscode-editor-background))',
							color: 'var(--vscode-sideBar-foreground, var(--vscode-foreground))',
						}}
					>
						<SessionSidebar />
						<SessionDialogs />
					</div>
				</TooltipProvider>
			</ErrorBoundary>
		</I18nProvider>
	);
}
