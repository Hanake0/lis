const noop = () => () => {};

export function emitSessionEvent(_event: string, _data?: unknown): void {}

export const sessionEvents = {
	onDeleteRequest: noop,
	onArchiveRequest: noop,
	onRenameRequest: noop,
	onShareRequest: noop,
	onUnshareRequest: noop,
	onExportRequest: noop,
	onForkRequest: noop,
	onDirectoryRequest: noop,
	onGitRefreshHint: noop,
	onMultiSelectDelete: noop,
	onMultiSelectArchive: noop,
	requestDelete: (..._a: any[]) => {},
	requestDirectoryDialog: (..._a: any[]) => {},
	requestGitRefresh: (..._a: any[]) => {},
	emit: (_event: string, _data?: unknown) => {},
};
