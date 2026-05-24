/**
 * Wires a webview to the extension-host REST/SSE plumbing. The webview cannot
 * make raw HTTP calls (CSP), so it posts `http_request` envelopes here; we run
 * the request via LisHttpClient and post back `http_response`. SSE events from
 * the global event stream are forwarded as `sse_event` envelopes.
 *
 * Both SidebarWebviewProvider and ChatPanelManager attach to the same bridge
 * instance per resolved webview.
 */

import type * as vscode from 'vscode';

import type { HttpRequest, LisHttpClient } from './lisHttpClient';
import type { LisEventStream, SseEvent }    from './lisEventStream';

interface HttpRequestEnvelope {
	type:       'http_request';
	request_id: string;
	method:     HttpRequest['method'];
	path:       string;
	query?:     HttpRequest['query'];
	body?:      unknown;
}

interface HttpResponseEnvelope {
	type:       'http_response';
	request_id: string;
	status:     number;
	body:       unknown;
}

interface SseEventEnvelope {
	type:    'sse_event';
	source:  'events' | 'chat';
	chat_id?: string;
	event:   string;
	data:    unknown;
	id?:     string;
}

interface ConnectionStateEnvelope {
	type:   'connection_state';
	events: 'connecting' | 'connected' | 'disconnected';
}

export class LisWebviewBridge {
	private dispose: (() => void)[] = [];

	constructor(
		private readonly webview: vscode.Webview,
		private readonly http:    LisHttpClient,
		private readonly events:  LisEventStream,
	) {
		this.dispose.push(
			this.webview.onDidReceiveMessage((msg: unknown) => this.handleIncoming(msg)).dispose,
		);

		// Forward every SSE event from /api/events to this webview.
		this.dispose.push(
			this.events.onEvent((ev: SseEvent) => this.postSse(ev)),
		);

		// Push connection state on every transition so the webview can render an offline banner.
		this.dispose.push(
			this.events.onState((state) => {
				const envelope: ConnectionStateEnvelope = { type: 'connection_state', events: state };
				void this.webview.postMessage(envelope);
			}),
		);
	}

	disposeAll(): void {
		for (const fn of this.dispose) {
			try { fn(); } catch { /* ignore */ }
		}
		this.dispose = [];
	}

	private async handleIncoming(msg: unknown): Promise<void> {
		if (!isHttpRequestEnvelope(msg)) return;

		const response = await this.http.request({
			method: msg.method,
			path:   msg.path,
			query:  msg.query,
			body:   msg.body,
		});

		const envelope: HttpResponseEnvelope = {
			type:       'http_response',
			request_id: msg.request_id,
			status:     response.status,
			body:       response.body,
		};
		await this.webview.postMessage(envelope);
	}

	private postSse(ev: SseEvent): void {
		const envelope: SseEventEnvelope = {
			type:   'sse_event',
			source: 'events',
			event:  ev.event,
			data:   ev.data,
			id:     ev.id,
		};
		void this.webview.postMessage(envelope);
	}
}

function isHttpRequestEnvelope(msg: unknown): msg is HttpRequestEnvelope {
	if (!msg || typeof msg !== 'object') return false;
	const m = msg as { type?: unknown };
	return m.type === 'http_request';
}
