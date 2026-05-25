import { useEffect } from 'react';
import { useChatStore } from './store';
import { MainLayout } from './components/layout/MainLayout';
import { TooltipProvider } from './components/ui/tooltip';
import { Toaster } from './components/ui/sonner';
import { ErrorBoundary } from './components/ui/ErrorBoundary';
import { I18nProvider } from './lib/i18n';
import { mountChatPanelBridge } from './sync/chat-panel-bridge';

/**
 * Chat-panel root. The extension host posts a single `init { chatId }` envelope
 * so this component knows which chat to bind to; everything else flows through
 * REST + SSE via {@link mountChatPanelBridge} (history, agents, current agent,
 * token stream, permissions, questions).
 */
export function App() {
	useEffect(() => {
		document.documentElement.classList.add('dark');
	}, []);

	useEffect(() => {
		const { setChatId } = useChatStore.getState();
		const handler = (ev: MessageEvent<{ type: string; [k: string]: unknown }>) => {
			const msg = ev.data;
			if (msg?.type === 'init' && typeof msg.chatId === 'string') setChatId(msg.chatId);
		};
		window.addEventListener('message', handler);
		return () => window.removeEventListener('message', handler);
	}, []);

	// Mount the bridge as soon as chatId is set; remount on chatId change so
	// the previous per-chat SSE stream is closed before a new one opens.
	const chatId = useChatStore((s) => s.chatId);
	useEffect(() => {
		if (!chatId) return;
		const stop = mountChatPanelBridge(chatId);
		return () => stop();
	}, [chatId]);

	return (
		<I18nProvider>
			<ErrorBoundary>
				<TooltipProvider delayDuration={300} skipDelayDuration={150}>
					<div className="h-full w-full text-foreground bg-background dark">
						<MainLayout />
						<Toaster />
					</div>
				</TooltipProvider>
			</ErrorBoundary>
		</I18nProvider>
	);
}
