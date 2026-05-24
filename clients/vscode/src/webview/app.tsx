import { useEffect } from 'preact/hooks';
import {
	chatId, connection, messages, agents, currentAgent,
	contextData, isTyping,
	composerModel, composerEffort, composerProfile,
	type ChatMessage, type TokenMeta,
} from './state';
import type { ConnectionState, ContextSegment } from '../types';
import { StatusBar } from './components/StatusBar';
import { MessageList } from './components/MessageList';
import { ContextPanel } from './components/ContextPanel';
import { Composer } from './components/composer/Composer';

export function App() {
	useEffect(() => {
		const handler = (ev: MessageEvent<{ type: string; [k: string]: unknown }>) => {
			const msg = ev.data;
			switch (msg.type) {
				case 'init':  chatId.value = msg.chatId as string; break;
				case 'state': connection.value = msg.state as ConnectionState; break;
				case 'frame': handleFrame(msg.frame as { type: string; [k: string]: unknown }); break;
			}
		};
		window.addEventListener('message', handler);
		return () => window.removeEventListener('message', handler);
	}, []);

	return (
		<div class="app">
			<StatusBar />
			<MessageList />
			<ContextPanel />
			<Composer />
		</div>
	);
}

function handleFrame(frame: { type: string; [k: string]: unknown }): void {
	switch (frame.type) {

		case 'message': {
			const role      = frame.role as string;
			const id        = frame.id as string;
			const text      = (frame.text as string | undefined) ?? '';
			const timestamp = (frame.timestamp as string | undefined) ?? '';

			if (role === 'assistant') {
				isTyping.value = false;
				const thinkTokens = frame.thinking_tokens as number | undefined;

				// Annotate the last non-streaming thinking block with token count
				let curr = messages.value;
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

				// Remove all streaming assistant bubbles (finalize stream)
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
				messages.value = curr;
				break;
			}

			if (role === 'user') {
				if (!messages.value.some(m => m.id === id))
					messages.value = [...messages.value, { kind: 'user', id, text, timestamp }];
				break;
			}

			if (role === 'tool') {
				if (messages.value.some(m => m.id === id)) break;
				const isErr = frame.tool_is_error as boolean | undefined;
				messages.value = [...messages.value, {
					kind:   'tool' as const,
					id,
					name:   (frame.tool_name as string | undefined) ?? 'tool',
					args:   frame.tool_args as string | undefined,
					result: isErr ? undefined : text,
					error:  isErr ? text || '(error)' : undefined,
					status: (isErr ? 'error' : 'done') as 'done' | 'error',
					tokens: frame.output_tokens as number | undefined,
				}];
				break;
			}
			break;
		}

		case 'chunk': {
			if (frame.done as boolean) break;
			const id    = frame.id as string;
			const delta = frame.delta as string;
			isTyping.value = false;
			const ex = messages.value.find(m => m.kind === 'assistant' && m.streaming && m.id === id);
			if (ex) {
				messages.value = messages.value.map(m =>
					m.kind === 'assistant' && m.id === id ? { ...m, text: m.text + delta } : m
				);
			} else {
				messages.value = [...messages.value, {
					kind: 'assistant' as const, id, text: delta, timestamp: '', streaming: true,
				}];
			}
			break;
		}

		case 'thinking_chunk': {
			const id    = frame.id as string;
			const delta = frame.delta as string;
			const done  = frame.done as boolean;
			isTyping.value = false;
			if (done) {
				messages.value = messages.value.map(m =>
					m.kind === 'thinking' && m.id === id ? { ...m, streaming: false, collapsed: true } : m
				);
				break;
			}
			if (messages.value.some(m => m.kind === 'thinking' && m.id === id)) {
				messages.value = messages.value.map(m =>
					m.kind === 'thinking' && m.id === id
						? { ...m, content: ((m as { content?: string }).content ?? '') + delta }
						: m
				);
			} else {
				messages.value = [...messages.value, {
					kind: 'thinking' as const, id, content: delta, collapsed: false, streaming: true,
				}];
			}
			break;
		}

		case 'tool_start': {
			const id = frame.id as string;
			if (messages.value.some(m => m.id === id)) break;
			messages.value = [...messages.value, {
				kind: 'tool' as const, id,
				name:   frame.tool_name as string,
				args:   frame.tool_args as string | undefined,
				status: 'running' as const,
			}];
			break;
		}

		case 'tool_result': {
			const id    = frame.id as string;
			const err   = frame.error as string | undefined;
			const result = frame.result as string | undefined;
			messages.value = messages.value.map(m =>
				m.kind === 'tool' && m.id === id
					? { ...m, result, error: err, status: (err ? 'error' : 'done') as 'done' | 'error' }
					: m
			);
			break;
		}

		case 'message_tokens': {
			const id  = frame.id as string;
			const tok = frame.output_tokens as number | undefined;
			if (typeof tok === 'number')
				messages.value = messages.value.map(m =>
					m.kind === 'tool' && m.id === id ? { ...m, tokens: tok } : m
				);
			break;
		}

		case 'typing':         isTyping.value = true;  break;
		case 'typing_stopped': isTyping.value = false; break;

		case 'chat_history': {
			const hist = frame.messages as Array<{
				id: string; role: string; text?: string; timestamp: string;
				input_tokens?: number; output_tokens?: number;
				cache_read_tokens?: number; cache_creation_tokens?: number;
				thinking_tokens?: number; thinking_content?: string;
				tool_name?: string; tool_args?: string; tool_is_error?: boolean;
			}>;
			isTyping.value = false;
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
			messages.value = next;
			break;
		}

		case 'agent_info': {
			composerModel.value   = (frame.model as string | undefined) ?? '';
			composerEffort.value  = (frame.effort as string | undefined) ?? '';
			composerProfile.value = (frame.tool_profile as string | undefined) ?? '';
			const found = agents.value.find(a => a.name === (frame.agent_name as string));
			if (found) currentAgent.value = found;
			break;
		}

		case 'agent_list':
			agents.value = frame.agents as typeof agents.value;
			break;

		case 'context':
			contextData.value = {
				total:    frame.total as number,
				budget:   frame.budget as number,
				segments: frame.segments as ContextSegment[],
				model:    frame.model as string,
				effort:   frame.effort as string | undefined,
			};
			break;

		case 'error':
			isTyping.value = false;
			messages.value = [...messages.value, {
				kind: 'error' as const,
				id:   `err_${Date.now()}`,
				text: (frame.error as string | undefined) ?? 'Unknown error',
			}];
			break;
	}
}
