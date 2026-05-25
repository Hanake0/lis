import { useChatStore } from '@/store';
import { LisRpcClient } from '@/lib/lis/rpc-client';
import type { PermissionResponse } from '@/types/permission';

/**
 * Session lifecycle actions exposed to OC-byte-identical components.
 * Every call delegates to {@link LisRpcClient} — REST against `/api/chats/*`
 * and `/api/permissions|questions/*`. The webview never speaks WS to the host.
 *
 * The optimistic store updates (sidebar list, removePermission/removeQuestion)
 * happen here so the UI reflects the action immediately; the authoritative
 * row state arrives shortly after via SSE on `/api/events` (chat lifecycle)
 * or per-chat SSE (permissions/questions follow-ups).
 */

export async function createSession(title?: string): Promise<string> {
	const chatId = `web:${crypto.randomUUID()}`;
	await LisRpcClient.instance().createChat({ chatId, name: title ?? 'New Chat' });
	return chatId;
}

export async function deleteSession(sessionId: string): Promise<void> {
	await LisRpcClient.instance().deleteChat(sessionId);
}

export async function archiveSession(sessionId: string): Promise<void> {
	await LisRpcClient.instance().archiveChat(sessionId);
}

export async function updateSessionTitle(sessionId: string, title: string): Promise<void> {
	await LisRpcClient.instance().updateChat(sessionId, { name: title });
}

export async function shareSession(_sessionId: string): Promise<void> {}
export async function unshareSession(_sessionId: string): Promise<void> {}
export async function optimisticSend(_params: unknown): Promise<void> {}
export async function refetchSessionMessages(_sessionId: string): Promise<void> {}

export async function abortCurrentOperation(_sessionId: string): Promise<void> {
	const chatId = useChatStore.getState().chatId;
	if (!chatId) return;
	await LisRpcClient.instance().abortChat(chatId);
}

export async function waitForConnectionOrThrow(): Promise<void> {
	const { connection } = useChatStore.getState();
	if (connection === 'connected') return;
	throw new Error(`Not connected (state: ${connection})`);
}

export async function respondToPermission(_sessionId: string, approvalId: string, decision: PermissionResponse): Promise<void> {
	const restDecision = decision === 'reject' ? 'deny' : decision;
	await LisRpcClient.instance().respondToPermission(approvalId, restDecision);
	useChatStore.getState().removePermission(approvalId);
}

export async function respondToQuestion(_sessionId: string, questionId: string, answers: string[][]): Promise<void> {
	await LisRpcClient.instance().respondToQuestion(questionId, { answers });
	useChatStore.getState().removeQuestion(questionId);
}

export async function rejectQuestion(_sessionId: string, questionId: string): Promise<void> {
	await LisRpcClient.instance().respondToQuestion(questionId, { rejected: true });
	useChatStore.getState().removeQuestion(questionId);
}
