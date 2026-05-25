import { vscode } from '@/vscodeApi';

/**
 * Compat-shim for the OC components ported into Lis. Most methods are no-ops or
 * return safe defaults (no SDK to back them), but the filesystem ops are real —
 * they post to the extension host's `host_fs` handler in [src/hostFs.ts] and
 * await a response correlated by `request_id`. Without that, the sidebar's
 * `useDirectoryStatusProbe` throws TypeError on every render with a project.
 */

interface FsEntry { name: string; path: string; isDirectory: boolean }
interface SystemInfo { home: string; platform: string }

interface HostFsResponse {
	type:        'host_fs_response';
	request_id:  string;
	ok:          boolean;
	result?:     unknown;
	error?:      string;
}

type Pending = { resolve: (v: unknown) => void; reject: (e: unknown) => void };
const pending = new Map<string, Pending>();

// One global listener — every host_fs_response routes through here. Installed
// lazily on first call so module init doesn't add listeners in environments
// (tests, SSR) where `window` may not be a real DOM.
let installed = false;
function ensureListener(): void {
	if (installed) return;
	installed = true;
	window.addEventListener('message', (ev: MessageEvent<unknown>) => {
		const msg = ev.data as { type?: string } | null;
		if (!msg || msg.type !== 'host_fs_response') return;
		const res = msg as HostFsResponse;
		const slot = pending.get(res.request_id);
		if (!slot) return;
		pending.delete(res.request_id);
		if (res.ok) slot.resolve(res.result);
		else        slot.reject(new Error(res.error ?? 'host_fs failed'));
	});
}

function callHost<T>(op: string, args: Record<string, unknown> = {}): Promise<T> {
	ensureListener();
	const request_id = crypto.randomUUID();
	return new Promise<T>((resolve, reject) => {
		pending.set(request_id, { resolve: resolve as (v: unknown) => void, reject });
		try {
			vscode.postMessage({ type: 'host_fs', op, request_id, ...args });
		} catch (err) {
			pending.delete(request_id);
			reject(err);
		}
	});
}

export const opencodeClient = {
	// ── Filesystem (real, via host bridge) ───────────────────────────

	listLocalDirectory: (path: string, _opts?: { respectGitignore?: boolean }): Promise<FsEntry[]> =>
		callHost<FsEntry[]>('list', { path }),

	probeDirectory: (path: string): Promise<boolean> =>
		callHost<boolean>('probe', { path }),

	createDirectory: (path: string, _opts?: { allowOutsideWorkspace?: boolean }): Promise<{ path: string }> =>
		callHost<{ path: string }>('create_directory', { path }),

	getSystemInfo: (): Promise<SystemInfo> =>
		callHost<SystemInfo>('system_info'),

	getFilesystemHome: (): Promise<string | null> =>
		callHost<string | null>('home'),

	// ── Stubs (no Lis backend yet — return safe defaults) ───────────

	getSdkClient: () => ({
		session: {
			create: async () => ({}),
			delete: async () => {},
			list:   async () => [],
			get:    async () => ({}),
			shell:  async () => {},
			chat:   async () => {},
			abort:  async () => {},
		},
	}),
	getScopedSdkClient: (_directory: string) => opencodeClient.getSdkClient(),
	getApiClient:       () => opencodeClient.getSdkClient(),
	getDirectory:       (): string | null => null,
	setDirectory:       (_directory: string | undefined): void => {},
	withDirectory:      <T>(_directory: string, fn: () => Promise<T> | T): Promise<T> | T => fn(),
	getHealth:          async (): Promise<{ planModeExperimentalEnabled: boolean }> => ({ planModeExperimentalEnabled: false }),
	listToolIds:        async (_opts?: { directory?: string }): Promise<string[]> => [],
	sendMessage:        async (_msg: unknown): Promise<void> => {},
	cloneRepository:    async (_opts: unknown): Promise<{ path: string }> => ({ path: '' }),
};
