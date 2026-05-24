import * as vscode from 'vscode';
import type { ConnectionState } from './types';

/**
 * Status-bar item showing the Lis connection state.
 * Click → opens VS Code settings filtered to the Lis section.
 */
export class LisStatusBar {
	private item: vscode.StatusBarItem;

	constructor() {
		this.item = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Left, 0);
		this.item.command = 'lis.configure';
		this.update('disconnected', '');
		this.item.show();
	}

	update(state: ConnectionState, baseOrigin: string): void {
		switch (state) {
			case 'connected':
				this.item.text = `$(plug) Lis`;
				this.item.tooltip = `Lis connected — ${baseOrigin || 'unknown server'}\nClick to configure`;
				this.item.color = new vscode.ThemeColor('statusBarItem.foreground');
				this.item.backgroundColor = undefined;
				break;
			case 'connecting':
				this.item.text = `$(sync~spin) Lis`;
				this.item.tooltip = `Lis connecting — ${baseOrigin || 'unknown server'}\nClick to configure`;
				this.item.color = undefined;
				this.item.backgroundColor = undefined;
				break;
			case 'disconnected':
			default:
				this.item.text = `$(debug-disconnect) Lis`;
				this.item.tooltip = `Lis disconnected — ${baseOrigin || 'not configured'}\nClick to configure`;
				this.item.color = undefined;
				this.item.backgroundColor = new vscode.ThemeColor('statusBarItem.warningBackground');
				break;
		}
	}

	dispose(): void {
		this.item.dispose();
	}
}
