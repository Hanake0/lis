import { useEffect } from 'react';
import { ChatContainer } from './components/chat/ChatContainer';
import { ChatErrorBoundary } from './components/chat/ChatErrorBoundary';
import { SessionDialogs } from './components/session/SessionDialogs';
import { TooltipProvider } from './components/ui/tooltip';
import { Toaster } from './components/ui/sonner';
import { ErrorBoundary } from './components/ui/ErrorBoundary';
import { I18nProvider } from './lib/i18n';

/**
 * Chat-panel root — one instance per VS Code chat WebviewPanel. The host posts
 * `init { chatId }` and the bridge (mounted from `index.ts` before the ready
 * ping fires) translates that into `useSessionUIStore.currentSessionId` and a
 * per-chat data subscription. Everything else (history, agent, token stream,
 * permissions, questions, sessions list) flows through REST + SSE.
 *
 * The OC `ChatContainer` is the entire UI — composer, message list, status
 * row, error boundaries. SessionDialogs hosts the delete / worktree confirm
 * surfaces (shared with the sidebar webview's instance).
 */
export function App() {
    useEffect(() => {
        document.documentElement.classList.add('dark');
    }, []);

    return (
        <I18nProvider>
            <ErrorBoundary>
                <TooltipProvider delayDuration={300} skipDelayDuration={150}>
                    <div className="h-full w-full text-foreground bg-background dark">
                        <ChatErrorBoundary>
                            {/* `autoOpenDraft={false}` disables OC's "if no
                              * session, open a draft" effect. The chat-panel
                              * bridge owns this state — it receives the init
                              * envelope from the host and calls
                              * `setCurrentSession(chatId)` (real chat) or
                              * sets `newSessionDraft={open:true}` (draft).
                              * Letting the OC effect also fire races the bridge
                              * and resets `currentSessionId` to null in the
                              * window between bridge mount and bridge init,
                              * silently breaking the send button.
                              */}
                            <ChatContainer autoOpenDraft={false} />
                        </ChatErrorBoundary>
                        <SessionDialogs />
                        <Toaster />
                    </div>
                </TooltipProvider>
            </ErrorBoundary>
        </I18nProvider>
    );
}
