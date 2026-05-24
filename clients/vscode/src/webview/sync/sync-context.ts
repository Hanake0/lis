// Adapter stubs for OC sync system

export type DirectorySyncState = {
	session: Array<{ id: string; title?: string; parentID?: string; directory?: string }>;
	session_status: Record<string, { type: string; message?: string; confirmedAt?: number } | undefined>;
	permission: Record<string, unknown[]>;
	question: Record<string, unknown[]>;
};

const EMPTY_STATE: DirectorySyncState = {
	session: [],
	session_status: {},
	permission: {},
	question: {},
};

const EMPTY_MESSAGES: Array<{ info: unknown; parts: unknown[] }> = [];

export function useDirectorySync<T>(selector: (state: DirectorySyncState) => T): T {
	return selector(EMPTY_STATE);
}

export function useSyncDirectory(): string {
	return '';
}

export function useSessions(): Array<{ id: string; title?: string; parentID?: string }> {
	return [];
}

export function useSessionMessages(_sessionId: string): unknown[] {
	return [];
}

export function useSessionMessagesResolved(_sessionId: string): boolean {
	return true;
}

export function useSessionMessageCount(_sessionId: string): number {
	return 0;
}

export function useSessionMessageRecords(_sessionId: string): Array<{ info: unknown; parts: unknown[] }> {
	return EMPTY_MESSAGES;
}

export function useSessionStatus(_sessionId: string): { type: string } | null {
	return null;
}

export function useUserMessageHistory(): unknown[] {
	return [];
}

export function setActiveSession(_sessionId: string): void {}

export class SyncProvider {
	static defaultProps = {};
}
export function buildSessionMessageRecordsSnapshot(..._args: any[]): any { return null; }
export function useAllLiveSessions(..._args: any[]): any { return null; }
export function useAllSessionStatuses(..._args: any[]): any { return null; }
export function useDirectoryStore(..._args: any[]): any { return null; }
export function useEnsureSessionMessages(..._args: any[]): any { return null; }
export function useGlobalSessionStatus(..._args: any[]): any { return null; }
export function useSession(..._args: any[]): any { return null; }
export function useSessionPermissions(..._args: any[]): any { return null; }
