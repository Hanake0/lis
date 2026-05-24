import * as vscode from 'vscode';

import { LisClient } from './lisClient';
import type { ChatSummary, ServerFrame } from './types';
import { relativeTime } from './util';

export class SessionItem extends vscode.TreeItem {
	constructor(
		public readonly chatId: string,
		public readonly name:    string | undefined,
		public readonly updatedAt: string | undefined,
	) {
		super(name && name.length > 0 ? name : chatId, vscode.TreeItemCollapsibleState.None);
		this.id          = chatId;
		this.description = updatedAt ? relativeTime(updatedAt) : undefined;
		this.tooltip     = `${name ?? chatId}\n${chatId}${updatedAt ? `\n${updatedAt}` : ''}`;
		this.iconPath    = new vscode.ThemeIcon('comment-discussion');
		this.contextValue = 'lis.session';
		this.command = {
			command: 'lis.openChat',
			title:   'Open Chat',
			arguments: [chatId, name ?? chatId],
		};
	}
}

export class SessionsTreeProvider implements vscode.TreeDataProvider<SessionItem> {
	private readonly _changeEmitter = new vscode.EventEmitter<SessionItem | undefined | void>();
	public  readonly onDidChangeTreeData = this._changeEmitter.event;

	// Ordered by most-recent first; map preserves insertion order so we rebuild on change.
	private sessions = new Map<string, ChatSummary>();
	private connected = false;

	constructor(private readonly client: LisClient) {
		client.onFrame(frame => this.onFrame(frame));
		client.onState(state => {
			const wasConnected = this.connected;
			this.connected = state === 'connected';
			if (!wasConnected && this.connected) this.requestList();
			this._changeEmitter.fire();
		});
	}

	getTreeItem(element: SessionItem): vscode.TreeItem { return element; }

	getChildren(): SessionItem[] {
		if (!this.connected && this.sessions.size === 0) {
			const placeholder = new SessionItem('__placeholder__', '(not connected)', undefined);
			placeholder.command     = undefined;
			placeholder.iconPath    = new vscode.ThemeIcon('debug-disconnect');
			placeholder.contextValue = 'lis.placeholder';
			return [placeholder];
		}
		return [...this.sessions.values()]
			.sort((a, b) => (b.updated_at ?? '').localeCompare(a.updated_at ?? ''))
			.map(s => new SessionItem(s.chat_id, s.name, s.updated_at));
	}

	refresh(): void {
		if (this.connected) this.requestList();
		this._changeEmitter.fire();
	}

	/**
	 * Insert a freshly-minted chat that the server doesn't know about yet, so it
	 * shows up in the sidebar immediately after the user hits +.
	 */
	addLocalChat(chatId: string, name?: string): void {
		if (this.sessions.has(chatId)) return;
		this.sessions.set(chatId, {
			chat_id:    chatId,
			name:       name ?? 'New Chat',
			updated_at: new Date().toISOString(),
		});
		this._changeEmitter.fire();
	}

	private requestList(): void {
		this.client.send({ type: 'list_chats' });
	}

	private onFrame(frame: ServerFrame): void {
		switch (frame.type) {
			case 'chat_list':
				this.sessions.clear();
				for (const c of frame.chats) this.sessions.set(c.chat_id, c);
				this._changeEmitter.fire();
				break;

			case 'chat_created':
				this.sessions.set(frame.chat_id, {
					chat_id:    frame.chat_id,
					name:       frame.name,
					updated_at: frame.timestamp,
				});
				this._changeEmitter.fire();
				break;

			case 'chat_deleted':
				this.sessions.delete(frame.chat_id);
				this._changeEmitter.fire();
				break;

			case 'chat_renamed': {
				const existing = this.sessions.get(frame.chat_id);
				if (existing) {
					this.sessions.set(frame.chat_id, { ...existing, name: frame.name });
					this._changeEmitter.fire();
				}
				break;
			}

			case 'chat_archived':
				this.sessions.delete(frame.chat_id);
				this._changeEmitter.fire();
				break;

			case 'message':
				this.sessions.set(frame.chat_id, {
					chat_id:    frame.chat_id,
					name:       this.sessions.get(frame.chat_id)?.name,
					updated_at: frame.timestamp ?? new Date().toISOString(),
				});
				this._changeEmitter.fire();
				break;
		}
	}
}
