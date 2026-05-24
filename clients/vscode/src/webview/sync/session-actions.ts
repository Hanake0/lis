import { vscode } from '@/vscodeApi';
import { useChatStore } from '@/store';
import type { PermissionResponse } from '@/types/permission';

export async function createSession(title?: string): Promise<string> {
	const chatId = `web:${crypto.randomUUID()}`;
	vscode.postMessage({ type: 'create_session', chat_id: chatId, name: title ?? 'New Chat' });
	return chatId;
}

export async function deleteSession(sessionId: string): Promise<void> {
	vscode.postMessage({ type: 'delete_session', chat_id: sessionId });
}

export async function archiveSession(sessionId: string): Promise<void> {
	vscode.postMessage({ type: 'archive_session', chat_id: sessionId });
}

export async function updateSessionTitle(sessionId: string, title: string): Promise<void> {
	vscode.postMessage({ type: 'rename_session', chat_id: sessionId, name: title });
}

export async function shareSession(_sessionId: string): Promise<void> {}
export async function unshareSession(_sessionId: string): Promise<void> {}
export async function optimisticSend(_params: unknown): Promise<void> {}
export async function refetchSessionMessages(_sessionId: string): Promise<void> {}

export async function abortCurrentOperation(_sessionId: string): Promise<void> {
	const chatId = useChatStore.getState().chatId;
	if (!chatId) return;
	vscode.postMessage({ type: 'send_frame', frame: { type: 'abort', chat_id: chatId } });
}

export async function waitForConnectionOrThrow(): Promise<void> {
	const { connection } = useChatStore.getState();
	if (connection === 'connected') return;
	throw new Error(`Not connected (state: ${connection})`);
}

export async function respondToPermission(_sessionId: string, approvalId: string, decision: PermissionResponse): Promise<void> {
	const wsDecision = decision === 'reject' ? 'deny' : decision;
	vscode.postMessage({
		type:  'send_frame',
		frame: { type: 'permission_response', approval_id: approvalId, decision: wsDecision },
	});
	useChatStore.getState().removePermission(approvalId);
}

export async function respondToQuestion(_sessionId: string, questionId: string, _answers: string[][]): Promise<void> {
	useChatStore.getState().removeQuestion(questionId);
}

export async function rejectQuestion(_sessionId: string, questionId: string): Promise<void> {
	useChatStore.getState().removeQuestion(questionId);
}
