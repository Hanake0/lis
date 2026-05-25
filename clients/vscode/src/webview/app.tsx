import { useEffect } from 'react';
import { ChatContainer } from './components/chat/ChatContainer';
import { ChatErrorBoundary } from './components/chat/ChatErrorBoundary';
import { SessionDialogs } from './components/session/SessionDialogs';
import { TooltipProvider } from './components/ui/tooltip';
import { Toaster } from './components/ui/sonner';
import { ErrorBoundary } from './components/ui/ErrorBoundary';
import { I18nProvider } from './lib/i18n';
import { installChatPanelInit } from './sync/chat-panel-bridge';

/**
 * Chat-panel root — one instance per VS Code chat WebviewPanel. The host posts
 * `init { chatId }` so the bridge knows which chat to bind to; everything
 * else (history, agent, token stream, permissions, questions, sessions list)
 * arrives through REST + SSE wired in {@link installChatPanelInit}.
 *
 * The OC `ChatContainer` is the entire UI — composer, message list, status
 * row, error boundaries. SessionDialogs hosts the delete / worktree confirm
 * surfaces (shared with the sidebar webview's instance).
 */
export function App() {
    useEffect(() => {
        document.documentElement.classList.add('dark');
        return installChatPanelInit();
    }, []);

    return (
        <I18nProvider>
            <ErrorBoundary>
                <TooltipProvider delayDuration={300} skipDelayDuration={150}>
                    <div className="h-full w-full text-foreground bg-background dark">
                        <ChatErrorBoundary>
                            <ChatContainer />
                        </ChatErrorBoundary>
                        <SessionDialogs />
                        <Toaster />
                    </div>
                </TooltipProvider>
            </ErrorBoundary>
        </I18nProvider>
    );
}
