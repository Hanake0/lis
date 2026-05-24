export type PermissionResponse = 'once' | 'always' | 'reject';

export interface PermissionRequest {
	id: string;
	sessionID: string;
	permission: string;
	patterns: string[];
	metadata: Record<string, unknown>;
	always: string[];
}
