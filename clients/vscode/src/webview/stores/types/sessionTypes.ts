export interface AttachedFile {
	id: string;
	name: string;
	path: string;
	mime?: string;
	url?: string;
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
export function getMemoryLimits(..._args: any[]): any { return null; }
