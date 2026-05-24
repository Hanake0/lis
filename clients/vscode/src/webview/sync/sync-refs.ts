export function getSyncSessions(): unknown[] { return []; }
export function getAllSyncSessions(): Array<{ id: string; title?: string; parentID?: string }> { return []; }
export function getSyncMessages(_sessionId: string, _directory?: string): unknown[] { return []; }
export function getSyncParts(_sessionId: string): unknown[] { return []; }
export function getDirectoryState(): unknown { return {}; }
export function getSyncChildStores(..._args: any[]): any { return null; }
export function getSyncSessionMaterializationStatus(..._args: any[]): any { return null; }
