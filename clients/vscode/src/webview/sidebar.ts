import { createElement } from 'react';
import { createRoot } from 'react-dom/client';
import { SidebarApp } from './sidebarApp';

createRoot(document.getElementById('app')!).render(createElement(SidebarApp, null));

declare function acquireVsCodeApi(): { postMessage: (msg: unknown) => void; getState: () => unknown; setState: (s: unknown) => void };
const vscode = acquireVsCodeApi();
(window as any).__vscode = vscode;
vscode.postMessage({ type: 'ready' });

window.addEventListener('message', (event) => {
	const msg = event.data;
	if (!msg || typeof msg !== 'object') return;

	const { useSidebarStore } = require('./sidebarStore');
	const store = useSidebarStore.getState();

	switch (msg.type) {
		case 'state':
			store.setConnected(msg.state === 'connected');
			break;

		case 'frame': {
			const frame = msg.frame;
			if (!frame) break;

			if (frame.type === 'chat_list') {
				store.setSessions(frame.chats ?? []);
			} else if (frame.type === 'chat_created') {
				store.addSession({ chat_id: frame.chat_id, name: frame.name, updated_at: frame.timestamp });
			} else if (frame.type === 'chat_deleted') {
				store.removeSession(frame.chat_id);
			} else if (frame.type === 'chat_renamed') {
				store.renameSession(frame.chat_id, frame.name);
			} else if (frame.type === 'chat_archived') {
				store.archiveSession(frame.chat_id, frame.archived_at);
			} else if (frame.type === 'agent_list') {
				store.setAgents(frame.agents ?? []);
			}
			break;
		}
	}
});
