export interface AttachedFile {
	id: string;
	file: File;
	filename: string;
	mimeType: string;
	size: number;
	dataUrl: string;
	source: 'local' | 'server' | 'vscode';
	serverPath?: string;
	vscodePath?: string;
	vscodeSource?: 'file' | 'selection';
}

export interface SessionContextUsage {
	totalTokens: number;
	percentage: number;
	contextLimit: number;
	outputLimit?: number;
	normalizedOutput?: number;
	thresholdLimit: number;
	lastMessageId?: string;
}

export interface SessionWorktreeAttachment {
	sessionId: string;
	worktreeRoot: string;
	branch: string;
}
export function getMemoryLimits(): { HISTORICAL_MESSAGES: number } { return { HISTORICAL_MESSAGES: 100 }; }
