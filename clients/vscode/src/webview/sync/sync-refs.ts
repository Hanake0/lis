const EMPTY_ARR: any[] = [];
const EMPTY_OBJ: Record<string, never> = {};
export function getSyncSessions(): unknown[] { return EMPTY_ARR; }
export function getAllSyncSessions(): Array<{ id: string; title?: string; parentID?: string }> { return EMPTY_ARR; }
export function getSyncMessages(_sessionId: string, _directory?: string): unknown[] { return EMPTY_ARR; }
export function getSyncParts(_sessionId: string): unknown[] { return EMPTY_ARR; }
export function getDirectoryState(): unknown { return EMPTY_OBJ; }
export function getSyncChildStores(..._args: any[]): any { return null; }
export function getSyncSessionMaterializationStatus(..._args: any[]): any { return null; }
