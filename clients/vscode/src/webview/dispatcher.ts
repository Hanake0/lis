// Frame dispatch — routes incoming frames to the appropriate handler.

import { appendError, hideTyping, renderMessage, showTyping, setStatus, clearMessagesDom } from './renderer';
import { handleChunk, handleThinkingChunk, finalizeStreamedMessage, setThinkingTokens, appendThinkingPlaceholder } from './streaming';
import { handleToolStart, handleToolResult, handleToolTokens } from './toolBlocks';
import { storeMetadata, attachMetadataBadge } from './metadata';
import { handleAgentInfo, handleAgentList } from './composer';
import { renderContext } from './contextPanel';

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
	thinking_content?:     string;
	tool_name?:            string;
	tool_args?:            string;
	tool_is_error?:        boolean;
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
				const thinkTokens = frame.thinking_tokens as number | undefined;
				if (thinkTokens && thinkTokens > 0) setThinkingTokens(thinkTokens);
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

		case 'message_tokens': {
			const out = frame.output_tokens as number | undefined;
			if (typeof out === 'number') handleToolTokens(frame.id as string, out);
			break;
		}

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

		case 'context':
			renderContext(
				frame.total as number,
				frame.budget as number,
				frame.segments as Array<{ name: string; tokens: number }>,
				frame.model as string,
				frame.effort as string | undefined,
			);
			break;

		case 'error':
			hideTyping();
			appendError(frame.error as string);
			break;
	}
}

function appendMessage(msg: Message, fromHistory = false): void {
	if (knownMessageIds.has(msg.id)) return;
	knownMessageIds.add(msg.id);

	// Tool-role messages render as collapsed tool blocks, not chat bubbles
	if (msg.role === 'tool') {
		if (msg.tool_name || msg.text) {
			handleToolStart(msg.id, msg.tool_name ?? 'tool', msg.tool_args, msg.output_tokens);
			// On history restore we don't have a separate error payload — the result text
			// IS the error message when tool_is_error is true (mirrors live behavior).
			const errorText = msg.tool_is_error ? (msg.text ?? '(error)') : undefined;
			const okText    = msg.tool_is_error ? undefined : msg.text;
			handleToolResult(msg.id, msg.tool_name ?? 'tool', okText, errorText);
		}
		messages.push(msg);
		return;
	}

	if (!hasText(msg)) return;

	// History-only: the live thinking block was created during streaming chunks
	// and is already in the DOM. On reload, no chunks fire — re-insert it above
	// the bubble using the thinking text we persisted server-side from the same
	// stream, so reload looks identical to live.
	if (fromHistory && msg.role === 'assistant'
		&& ((msg.thinking_tokens ?? 0) > 0 || (msg.thinking_content && msg.thinking_content.length > 0))) {
		appendThinkingPlaceholder(msg.thinking_tokens ?? 0, msg.thinking_content);
	}

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
	// Render through appendMessage so tool-role rows produce tool blocks (not bubbles)
	// and assistant rows attach metadata badges, instead of bypassing to a flat renderAll.
	clearMessagesDom();
	for (const m of list) appendMessage(m, true);
}

function hasText(msg: { text?: string }): boolean {
	return typeof msg.text === 'string' && msg.text.trim().length > 0;
}
