import { create } from 'zustand';

/**
 * Self-update store. In OC's desktop runtime this drives the
 * electron-updater flow (download, progress, restart-to-install). In the
 * VS Code extension, updates flow through the marketplace via the VS Code
 * extension host — the webview has no role to play and there is no Lis
 * backend involvement. So every action here is a no-op and the boolean
 * flags stay `false`.
 *
 * The surface is typed (not `any`) so byte-identical OC consumers like
 * `AboutSettings`, `MainLayout`, `VSCodeLayout`, and `SessionSidebar`
 * compile against a stable shape. The OC desktop bundle ships its own
 * implementation; only the VS Code bundle uses this no-op.
 */

export interface UpdateInfo {
	version:        string;
	releaseDate?:   string;
	releaseName?:   string;
	releaseNotes?:  string;
}

export interface UpdateProgress {
	percent:        number;
	bytesPerSecond: number;
	transferred:    number;
	total:          number;
}

interface UpdateStore {
	runtimeType:  'vscode' | 'desktop' | 'web';
	available:    boolean;
	info:         UpdateInfo | null;
	downloading:  boolean;
	downloaded:   boolean;
	progress:     UpdateProgress | null;
	error:        string | null;

	checkForUpdates: () => Promise<void>;
	downloadUpdate:  () => Promise<void>;
	restartToUpdate: () => void;
}

export const useUpdateStore = create<UpdateStore>(() => ({
	runtimeType:     'vscode',
	available:       false,
	info:            null,
	downloading:     false,
	downloaded:      false,
	progress:        null,
	error:           null,

	checkForUpdates: async () => { /* extensions update via marketplace */ },
	downloadUpdate:  async () => { /* extensions update via marketplace */ },
	restartToUpdate: ()       => { /* extensions update via marketplace */ },
}));
