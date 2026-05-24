export interface PermissionRequest {
	id: string;
	sessionId: string;
	tool: string;
	args?: string;
	description?: string;
}
