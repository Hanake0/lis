export async function createSession(): Promise<void> {}
export async function deleteSession(_sessionId: string): Promise<void> {}
export async function archiveSession(_sessionId: string): Promise<void> {}
export async function updateSessionTitle(_sessionId: string, _title: string): Promise<void> {}
export async function shareSession(_sessionId: string): Promise<void> {}
export async function unshareSession(_sessionId: string): Promise<void> {}
export async function optimisticSend(_params: unknown): Promise<void> {}
export async function refetchSessionMessages(_sessionId: string): Promise<void> {}
