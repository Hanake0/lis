import { SessionSidebar } from './components/session/SessionSidebar';
import { SessionDialogs } from './components/session/SessionDialogs';
import { TooltipProvider } from './components/ui/tooltip';
import { ErrorBoundary } from './components/ui/ErrorBoundary';
import { I18nProvider } from './lib/i18n';
import { vscode } from './vscodeApi';
import { useSessionUIStore } from './sync/session-ui-store';

/**
 * Side effects required before the sidebar renders:
 *  - dark + lis-sidebar CSS scope on `<html>` so design-system tokens resolve.
 *  - Intercept `setCurrentSession` to send `open_session` to the extension host,
 *    which routes the click to `ChatPanelManager.openOrFocus`.
 *
 * Project state comes from `useProjectsStore` (wired via `installProjectsSync`
 * in webview/index.ts), session lifecycle comes from `useGlobalSessionsStore`
 * (wired via `chat-events-bridge`). The legacy synthetic Lis-project hack and
 * the WS-frame postMessage handler that used to live here are both gone.
 */
export function installSidebarFrameHandler(): void {
	document.documentElement.classList.add('dark', 'lis-sidebar');

	const orig = useSessionUIStore.getState().setCurrentSession;
	useSessionUIStore.setState({
		setCurrentSession: (id: string) => {
			vscode.postMessage({ type: 'open_session', chat_id: id });
			orig(id);
		},
	});
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
