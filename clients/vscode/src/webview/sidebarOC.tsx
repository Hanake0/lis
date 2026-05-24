import { useEffect } from 'react';
import { useChatStore } from './store';
import type { ConnectionState } from '../types';
import { SessionSidebar } from './components/session/SessionSidebar';
import { SessionDialogs } from './components/session/SessionDialogs';
import { TooltipProvider } from './components/ui/tooltip';
import { ErrorBoundary } from './components/ui/ErrorBoundary';
import { I18nProvider } from './lib/i18n';
import { vscode } from './vscodeApi';
import { useSessionUIStore } from './sync/session-ui-store';
import { useGlobalSessionsStore } from './stores/useGlobalSessionsStore';

export function installSidebarFrameHandler(): void {
	document.documentElement.classList.add('dark');

	window.addEventListener('message', (ev: MessageEvent<{ type: string; [k: string]: unknown }>) => {
		const msg = ev.data;
		if (!msg || typeof msg !== 'object') return;
		switch (msg.type) {
			case 'state':
				useChatStore.getState().setConnection(msg.state as ConnectionState);
				break;
			case 'frame':
				handleSidebarFrame(msg.frame as { type: string; [k: string]: unknown });
				break;
		}
	});

	// Intercept session selection to open in editor tab
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
					<div className="h-full w-full text-foreground bg-background dark overflow-hidden">
						<SessionSidebar />
						<SessionDialogs />
					</div>
				</TooltipProvider>
			</ErrorBoundary>
		</I18nProvider>
	);
}

function handleSidebarFrame(frame: { type: string; [k: string]: unknown }): void {
	const store = useChatStore.getState();

	switch (frame.type) {
		case 'chat_list': {
			const raw = (frame.chats as Array<{ chat_id: string; name?: string; updated_at?: string; archived_at?: string }>) ?? [];
			store.setSessions(raw);
			syncToGlobalStore(raw);
			break;
		}

		case 'chat_created': {
			const chatId = frame.chat_id as string;
			if (!store.sessions.some((s: any) => s.chat_id === chatId)) {
				const next = [{
					chat_id: chatId,
					name: frame.name as string | undefined,
					updated_at: (frame.timestamp as string) ?? new Date().toISOString(),
				}, ...store.sessions];
				store.setSessions(next);
				syncToGlobalStore(next);
			}
			break;
		}

		case 'chat_deleted': {
			const next = store.sessions.filter((s: any) => s.chat_id !== (frame.chat_id as string));
			store.setSessions(next);
			syncToGlobalStore(next);
			break;
		}

		case 'chat_renamed': {
			const next = store.sessions.map((s: any) =>
				s.chat_id === (frame.chat_id as string) ? { ...s, name: frame.name as string } : s
			);
			store.setSessions(next);
			syncToGlobalStore(next);
			break;
		}

		case 'chat_archived': {
			const next = store.sessions.map((s: any) =>
				s.chat_id === (frame.chat_id as string) ? { ...s, archived_at: frame.archived_at as string } : s
			);
			store.setSessions(next);
			syncToGlobalStore(next);
			break;
		}

		case 'agent_list': {
			const agents = (frame.agents as Array<{ name: string; display_name?: string; model?: string }>) ?? [];
			store.setAgents(agents);
			const defaultAgent = agents.find((a: any) => a.is_default) ?? agents[0];
			if (defaultAgent && !store.currentAgent) {
				store.setCurrentAgent(defaultAgent);
				store.setComposerModel(defaultAgent.model ?? '');
			}
			break;
		}

		case 'agent_info': {
			const agent = {
				name: frame.agent_name as string,
				display_name: frame.display_name as string | undefined,
				model: frame.model as string | undefined,
			};
			store.setCurrentAgent(agent);
			if (agent.model) store.setComposerModel(agent.model);
			break;
		}
	}
}

function syncToGlobalStore(raw: Array<{ chat_id: string; name?: string; updated_at?: string; archived_at?: string }>): void {
	const toOCSession = (s: typeof raw[number]) => ({
		id: s.chat_id,
		title: s.name,
		time: {
			updated: s.updated_at ? new Date(s.updated_at).getTime() : undefined,
			archived: s.archived_at ? new Date(s.archived_at).getTime() : undefined,
		},
	});
	const active = raw.filter(s => !s.archived_at).map(toOCSession);
	const archived = raw.filter(s => s.archived_at).map(toOCSession);
	useGlobalSessionsStore.setState({
		sessions: raw.map(toOCSession),
		activeSessions: active,
		globalActiveSessions: active,
		archivedSessions: archived,
		hasLoaded: true,
	});
}
