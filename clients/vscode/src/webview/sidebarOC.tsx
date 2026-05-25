import { SessionSidebar } from './components/session/SessionSidebar';
import { SessionDialogs } from './components/session/SessionDialogs';
import { TooltipProvider } from './components/ui/tooltip';
import { ErrorBoundary } from './components/ui/ErrorBoundary';
import { I18nProvider } from './lib/i18n';
import { vscode } from './vscodeApi';
import { useSessionUIStore } from './sync/session-ui-store';
import { useProjectsStore } from './stores/useProjectsStore';

const LIS_PROJECT_ID = 'lis';
const LIS_PROJECT_PATH = '/lis';

/**
 * Sidebar uses a single synthetic "Lis" project until Stage 3's REST projects
 * land in the sidebar UI. Once `useProjectsStore` is wired to `/api/projects`
 * the seed disappears and real projects flow in from the backend.
 */
function ensureLisProject(): void {
	const state = useProjectsStore.getState();
	if (state.projects?.some((p: any) => p.id === LIS_PROJECT_ID)) return;
	useProjectsStore.setState({
		projects: [{ id: LIS_PROJECT_ID, path: LIS_PROJECT_PATH, label: 'Lis' }],
		activeProjectId: LIS_PROJECT_ID,
	});
}

/**
 * Side effects required before the sidebar renders:
 *  - dark + lis-sidebar CSS scope on `<html>` so design-system tokens resolve.
 *  - One-off "Lis" project so the sidebar has a project to group sessions under.
 *  - Intercept `setCurrentSession` to send `open_session` to the extension host,
 *    which routes the click to `ChatPanelManager.openOrFocus`.
 *
 * Frame/state postMessage handling that previously lived here is dead — the
 * sidebar now reads its session list from `useGlobalSessionsStore`, which is
 * fed by the global SSE bridge in [sync/chat-events-bridge.ts](./sync/chat-events-bridge.ts).
 */
export function installSidebarFrameHandler(): void {
	document.documentElement.classList.add('dark', 'lis-sidebar');
	ensureLisProject();

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
