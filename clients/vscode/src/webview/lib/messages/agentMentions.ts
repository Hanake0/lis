/**
 * OC's ChatInput consumes this as `{ sanitizedText, mention }` (see
 * `handleSubmit` at clients/vscode/src/webview/components/chat/ChatInput.tsx).
 * The previous Lis stub returned `{ text, mentions }` — destructuring
 * `sanitizedText` against the wrong shape evaluated to `undefined`, which
 * then bypassed the empty-content guard and silently bailed the send.
 *
 * We treat agent mentions as a no-op for now (no @agent rewrites) but expose
 * the shape OC expects so the call site behaves correctly. When real agent
 * resolution lands, the body becomes the matching logic — the signature
 * stays.
 */
export function parseAgentMentions(text: string, _agents?: unknown): { sanitizedText: string; mention: { name: string } | null } {
	return { sanitizedText: text ?? '', mention: null };
}
