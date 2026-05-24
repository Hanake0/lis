// Frame dispatch — routes incoming frames to the appropriate handler.

import { appendSystem, hideTyping, renderMessage, renderAll, showTyping, setStatus } from './renderer';
import { handleChunk, handleThinkingChunk, finalizeStreamedMessage } from './streaming';
import { handleToolStart, handleToolResult } from './toolBlocks';
import { storeMetadata, attachMetadataBadge } from './metadata';
import { handleAgentInfo, handleAgentList } from './composer';

interface Message {
	id:                    string;
	role:                  string;
	text:                  string;
	timestamp:             string;
	input_tokens?:         number;
	output_tokens?:        number;
	cache_read_tokens?:    number;
	cache_creation_tokens?: number;
	thinking_tokens?:      number;
}

let chatId: string | null = null;
const messages: Message[] = [];
const knownMessageIds = new Set<string>();

export function getChatId(): string | null { return chatId; }

export function handleHostMessage(msg: { type: string; [key: string]: unknown }): void {
	switch (msg.type) {
		case 'init':  chatId = msg.chatId as string; break;
		case 'state': setStatus(msg.state as string); break;
		case 'frame': handleFrame(msg.frame as { type: string; [key: string]: unknown }); break;
	}
}

function handleFrame(frame: { type: string; [key: string]: unknown }): void {
	switch (frame.type) {
		case 'message':
			if (frame.role === 'assistant') {
				hideTyping();
				finalizeStreamedMessage(frame.id as string, frame.text as string);
			}
			appendMessage({
				id:                    frame.id as string,
				role:                  frame.role as string,
				text:                  frame.text as string,
				timestamp:             frame.timestamp as string,
				input_tokens:          frame.input_tokens as number | undefined,
				output_tokens:         frame.output_tokens as number | undefined,
				cache_read_tokens:     frame.cache_read_tokens as number | undefined,
				cache_creation_tokens: frame.cache_creation_tokens as number | undefined,
				thinking_tokens:       frame.thinking_tokens as number | undefined,
			});
			break;

		case 'chunk':
			handleChunk(frame.id as string, frame.delta as string, frame.done as boolean);
			break;

		case 'thinking_chunk':
			handleThinkingChunk(frame.id as string, frame.delta as string, frame.done as boolean);
			break;

		case 'tool_start':
			handleToolStart(frame.id as string, frame.tool_name as string, frame.tool_args as string | undefined);
			break;

		case 'tool_result':
			handleToolResult(frame.id as string, frame.tool_name as string, frame.result as string | undefined, frame.error as string | undefined);
			break;

		case 'typing':         showTyping(); break;
		case 'typing_stopped': hideTyping(); break;

		case 'chat_history':
			replaceHistory(frame.messages as Message[]);
			break;

		case 'agent_info':
			handleAgentInfo(
				frame.agent_name as string,
				frame.model as string,
				frame.effort as string | undefined,
				frame.tool_profile as string | undefined,
			);
			break;

		case 'agent_list':
			handleAgentList(frame.agents as Array<{ name: string; display_name?: string; model: string; effort?: string; tool_profile?: string; is_default: boolean }>);
			break;

		case 'error':
			hideTyping();
			appendSystem(`error: ${frame.error}`);
			break;
	}
}

function appendMessage(msg: Message): void {
	if (knownMessageIds.has(msg.id)) return;
	if (!hasText(msg)) return;
	knownMessageIds.add(msg.id);
	messages.push(msg);
	const el = renderMessage(msg);

	if (msg.input_tokens || msg.output_tokens) {
		storeMetadata(msg.id, {
			input_tokens:          msg.input_tokens,
			output_tokens:         msg.output_tokens,
			cache_read_tokens:     msg.cache_read_tokens,
			cache_creation_tokens: msg.cache_creation_tokens,
			thinking_tokens:       msg.thinking_tokens,
		});
		attachMetadataBadge(el, msg.id);
	}
}

function replaceHistory(list: Message[]): void {
	messages.length = 0;
	knownMessageIds.clear();
	hideTyping();
	for (const m of list) {
		if (!hasText(m)) continue;
		messages.push(m);
		knownMessageIds.add(m.id);
	}
	renderAll(messages);
}

function hasText(msg: { text?: string }): boolean {
	return typeof msg.text === 'string' && msg.text.trim().length > 0;
}
