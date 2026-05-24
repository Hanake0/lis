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
		case 'chat_list':
			store.setSessions(frame.chats as typeof store.sessions);
			break;

		case 'chat_created': {
			const chatId = frame.chat_id as string;
			if (!store.sessions.some(s => s.chat_id === chatId)) {
				store.setSessions([{
					chat_id: chatId,
					name: frame.name as string | undefined,
					updated_at: (frame.timestamp as string) ?? new Date().toISOString(),
				}, ...store.sessions]);
			}
			break;
		}

		case 'chat_deleted':
			store.setSessions(store.sessions.filter(s => s.chat_id !== (frame.chat_id as string)));
			break;

		case 'chat_renamed':
			store.setSessions(store.sessions.map(s =>
				s.chat_id === (frame.chat_id as string) ? { ...s, name: frame.name as string } : s
			));
			break;

		case 'chat_archived':
			store.setSessions(store.sessions.map(s =>
				s.chat_id === (frame.chat_id as string) ? { ...s, archived_at: frame.archived_at as string } : s
			));
			break;

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
