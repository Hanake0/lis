import { useEffect } from 'react';
import { useChatStore, type ChatMessage, type TokenMeta } from './store';
import type { ConnectionState, ContextSegment } from '../types';
import { MainLayout } from './components/layout/MainLayout';
import { TooltipProvider } from './components/ui/tooltip';
import { Toaster } from './components/ui/sonner';
import { ErrorBoundary } from './components/ui/ErrorBoundary';

export function App() {
	useEffect(() => {
		const { setChatId, setConnection } = useChatStore.getState();
		const handler = (ev: MessageEvent<{ type: string; [k: string]: unknown }>) => {
			const msg = ev.data;
			switch (msg.type) {
				case 'init':  setChatId(msg.chatId as string); break;
				case 'state': setConnection(msg.state as ConnectionState); break;
				case 'frame': handleFrame(msg.frame as { type: string; [k: string]: unknown }); break;
			}
		};
		window.addEventListener('message', handler);
		return () => window.removeEventListener('message', handler);
	}, []);

	return (
		<ErrorBoundary>
			<TooltipProvider delayDuration={300} skipDelayDuration={150}>
				<div className="h-full w-full text-foreground bg-background dark">
					<MainLayout />
					<Toaster />
				</div>
			</TooltipProvider>
		</ErrorBoundary>
	);
}

function handleFrame(frame: { type: string; [k: string]: unknown }): void {
	const store = useChatStore.getState();

	switch (frame.type) {

		case 'message': {
			const role      = frame.role as string;
			const id        = frame.id as string;
			const text      = (frame.text as string | undefined) ?? '';
			const timestamp = (frame.timestamp as string | undefined) ?? '';

			if (role === 'assistant') {
				store.setIsTyping(false);
				const thinkTokens = frame.thinking_tokens as number | undefined;

				let curr = store.messages;
				if (thinkTokens && thinkTokens > 0) {
					let idx = -1;
					for (let i = curr.length - 1; i >= 0; i--) {
						if (curr[i].kind === 'thinking' && !(curr[i] as { streaming?: boolean }).streaming) {
							idx = i; break;
						}
					}
					if (idx >= 0) {
						curr = [...curr];
						const t = curr[idx];
						if (t.kind === 'thinking') curr[idx] = { ...t, tokens: thinkTokens };
					}
				}

				curr = curr.filter(m => !(m.kind === 'assistant' && m.streaming));

				if (!curr.some(m => m.id === id)) {
					curr = [...curr, {
						kind: 'assistant' as const,
						id, text, timestamp, streaming: false,
						tokens: {
							input_tokens:          frame.input_tokens as number | undefined,
							output_tokens:         frame.output_tokens as number | undefined,
							cache_read_tokens:     frame.cache_read_tokens as number | undefined,
							cache_creation_tokens: frame.cache_creation_tokens as number | undefined,
							thinking_tokens:       thinkTokens,
						} as TokenMeta,
					}];
				}
				store.setMessages(curr);
				break;
			}

			if (role === 'user') {
				if (!store.messages.some(m => m.id === id))
					store.setMessages([...store.messages, { kind: 'user', id, text, timestamp }]);
				break;
			}

			if (role === 'tool') {
				if (store.messages.some(m => m.id === id)) break;
				const isErr = frame.tool_is_error as boolean | undefined;
				store.setMessages([...store.messages, {
					kind:   'tool' as const,
					id,
					name:   (frame.tool_name as string | undefined) ?? 'tool',
					args:   frame.tool_args as string | undefined,
					result: isErr ? undefined : text,
					error:  isErr ? text || '(error)' : undefined,
					status: (isErr ? 'error' : 'done') as 'done' | 'error',
					tokens: frame.output_tokens as number | undefined,
				}]);
				break;
			}
			break;
		}

		case 'chunk': {
			if (frame.done as boolean) break;
			const id    = frame.id as string;
			const delta = frame.delta as string;
			store.setIsTyping(false);
			const ex = store.messages.find(m => m.kind === 'assistant' && m.streaming && m.id === id);
			if (ex) {
				store.setMessages(store.messages.map(m =>
					m.kind === 'assistant' && m.id === id ? { ...m, text: m.text + delta } : m
				));
			} else {
				store.setMessages([...store.messages, {
					kind: 'assistant' as const, id, text: delta, timestamp: '', streaming: true,
				}]);
			}
			break;
		}

		case 'thinking_chunk': {
			const id    = frame.id as string;
			const delta = frame.delta as string;
			const done  = frame.done as boolean;
			store.setIsTyping(false);
			if (done) {
				store.setMessages(store.messages.map(m =>
					m.kind === 'thinking' && m.id === id ? { ...m, streaming: false, collapsed: true } : m
				));
				break;
			}
			if (store.messages.some(m => m.kind === 'thinking' && m.id === id)) {
				store.setMessages(store.messages.map(m =>
					m.kind === 'thinking' && m.id === id
						? { ...m, content: ((m as { content?: string }).content ?? '') + delta }
						: m
				));
			} else {
				store.setMessages([...store.messages, {
					kind: 'thinking' as const, id, content: delta, collapsed: false, streaming: true,
				}]);
			}
			break;
		}

		case 'tool_start': {
			const id = frame.id as string;
			if (store.messages.some(m => m.id === id)) break;
			store.setMessages([...store.messages, {
				kind: 'tool' as const, id,
				name:   frame.tool_name as string,
				args:   frame.tool_args as string | undefined,
				status: 'running' as const,
			}]);
			break;
		}

		case 'tool_result': {
			const id     = frame.id as string;
			const err    = frame.error as string | undefined;
			const result = frame.result as string | undefined;
			store.setMessages(store.messages.map(m =>
				m.kind === 'tool' && m.id === id
					? { ...m, result, error: err, status: (err ? 'error' : 'done') as 'done' | 'error' }
					: m
			));
			break;
		}

		case 'message_tokens': {
			const id  = frame.id as string;
			const tok = frame.output_tokens as number | undefined;
			if (typeof tok === 'number')
				store.setMessages(store.messages.map(m =>
					m.kind === 'tool' && m.id === id ? { ...m, tokens: tok } : m
				));
			break;
		}

		case 'typing':         store.setIsTyping(true);  break;
		case 'typing_stopped': store.setIsTyping(false); break;

		case 'chat_history': {
			const hist = frame.messages as Array<{
				id: string; role: string; text?: string; timestamp: string;
				input_tokens?: number; output_tokens?: number;
				cache_read_tokens?: number; cache_creation_tokens?: number;
				thinking_tokens?: number; thinking_content?: string;
				tool_name?: string; tool_args?: string; tool_is_error?: boolean;
			}>;
			store.setIsTyping(false);
			const next: ChatMessage[] = [];
			for (const m of hist) {
				if (m.role === 'tool') {
					if (!m.tool_name && !m.text) continue;
					next.push({
						kind:   'tool', id: m.id,
						name:   m.tool_name ?? 'tool',
						args:   m.tool_args,
						result: m.tool_is_error ? undefined : m.text,
						error:  m.tool_is_error ? (m.text ?? '(error)') : undefined,
						status: m.tool_is_error ? 'error' : 'done',
						tokens: m.output_tokens,
					});
				} else if (m.role === 'assistant' && m.text?.trim()) {
					if ((m.thinking_tokens ?? 0) > 0 || (m.thinking_content?.length ?? 0) > 0) {
						next.push({
							kind: 'thinking', id: `${m.id}__thinking`,
							content: m.thinking_content ?? '',
							collapsed: true, streaming: false,
							tokens: m.thinking_tokens,
						});
					}
					next.push({
						kind: 'assistant', id: m.id,
						text: m.text ?? '', timestamp: m.timestamp, streaming: false,
						tokens: {
							input_tokens:          m.input_tokens,
							output_tokens:         m.output_tokens,
							cache_read_tokens:     m.cache_read_tokens,
							cache_creation_tokens: m.cache_creation_tokens,
							thinking_tokens:       m.thinking_tokens,
						},
					});
				} else if (m.role === 'user' && m.text?.trim()) {
					next.push({ kind: 'user', id: m.id, text: m.text ?? '', timestamp: m.timestamp });
				}
			}
			store.setMessages(next);
			break;
		}

		case 'agent_info': {
			store.setComposerModel((frame.model as string | undefined) ?? '');
			store.setComposerEffort((frame.effort as string | undefined) ?? '');
			store.setComposerProfile((frame.tool_profile as string | undefined) ?? '');
			const found = store.agents.find(a => a.name === (frame.agent_name as string));
			if (found) store.setCurrentAgent(found);
			break;
		}

		case 'agent_list':
			store.setAgents(frame.agents as ReturnType<typeof useChatStore.getState>['agents']);
			break;

		case 'context':
			store.setContextData({
				total:    frame.total as number,
				budget:   frame.budget as number,
				segments: frame.segments as ContextSegment[],
				model:    frame.model as string,
				effort:   frame.effort as string | undefined,
			});
			break;

		case 'error':
			store.setIsTyping(false);
			store.setMessages([...store.messages, {
				kind: 'error' as const,
				id:   `err_${Date.now()}`,
				text: (frame.error as string | undefined) ?? 'Unknown error',
			}]);
			break;
	}
}
