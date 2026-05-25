import { create } from 'zustand';

import { LisRpcClient, LisRpcError } from '@/lib/lis/rpc-client';

/**
 * Lis-native projects store. Reads from `GET /api/projects` on bootstrap,
 * subscribes to SSE events (project_created/updated/deleted, chat_moved) so
 * the sidebar stays in sync without polling, and forwards mutations through
 * the REST surface via {@link LisRpcClient}.
 *
 * The shape mirrors the OC `useProjectsStore` API (so OC components like
 * `ProjectsList` / `ProjectsManager` can consume it unchanged) but drops the
 * desktop-only persistence / homeDirectory / PROJECT_COLORS plumbing that
 * doesn't apply in the VS Code webview.
 */

export interface ProjectEntry {
	id:                    string;
	path:                  string;
	label:                 string | null;
	icon?:                 string | null;
	icon_image_mime?:      string | null;
	icon_image_source?:    string | null;
	icon_image_updated_at?: string | null;
	icon_background?:      string | null;
	color?:                string | null;
	added_at:              string;
	last_opened_at?:       string | null;
	created_at:            string;
	updated_at:            string;
	sidebar_collapsed:     boolean;
	sort_index:            number;
}

interface UpdateMeta {
	label?:           string | null;
	color?:           string | null;
	icon?:            string | null;
	iconBackground?:  string | null;
	sidebarCollapsed?: boolean;
}

interface ProjectsStore {
	projects:                 ProjectEntry[];
	activeProjectId:          string | null;
	loading:                  boolean;
	lastError:                string | null;

	loadAll:                  () => Promise<void>;
	setActiveProject:         (id: string | null) => void;
	setActiveProjectIdOnly:   (id: string | null) => void;
	getActiveProject:         () => ProjectEntry | null;

	addProject:               (path: string, options?: { label?: string; color?: string }) => Promise<ProjectEntry | null>;
	removeProject:            (id: string) => Promise<void>;
	updateProjectMeta:        (id: string, meta: UpdateMeta) => Promise<void>;
	reorderProjects:          (fromIndex: number, toIndex: number) => Promise<void>;
	touchProject:             (id: string) => Promise<void>;
	uploadProjectIcon:        (id: string, file: File, source?: 'custom' | 'auto') => Promise<{ ok: boolean; error?: string }>;
	removeProjectIcon:        (id: string) => Promise<{ ok: boolean; error?: string }>;

	/** Internal — wired by {@link installProjectsSync}. */
	_setFromServer:           (projects: ProjectEntry[]) => void;
	_applyProjectCreated:     (project: ProjectEntry) => void;
	_applyProjectUpdated:     (project: ProjectEntry) => void;
	_applyProjectDeleted:     (id: string) => void;
}

function sortByIndex(list: ProjectEntry[]): ProjectEntry[] {
	return [...list].sort((a, b) => {
		if (a.sort_index !== b.sort_index) return a.sort_index - b.sort_index;
		return a.created_at.localeCompare(b.created_at);
	});
}

export const useProjectsStore = create<ProjectsStore>((set, get) => ({
	projects:        [],
	activeProjectId: null,
	loading:         false,
	lastError:       null,

	loadAll: async () => {
		set({ loading: true, lastError: null });
		try {
			const all = await LisRpcClient.instance().listProjects() as ProjectEntry[];
			set({ projects: sortByIndex(all), loading: false });
		} catch (err) {
			set({ loading: false, lastError: errorMessage(err) });
		}
	},

	setActiveProject: (id) => {
		set({ activeProjectId: id });
		if (id) void LisRpcClient.instance().touchProject(id).catch(() => { /* silent */ });
	},

	setActiveProjectIdOnly: (id) => set({ activeProjectId: id }),

	getActiveProject: () => {
		const { projects, activeProjectId } = get();
		return projects.find(p => p.id === activeProjectId) ?? null;
	},

	addProject: async (path, options) => {
		try {
			const created = await LisRpcClient.instance().createProject({ path, label: options?.label, color: options?.color }) as ProjectEntry;
			get()._applyProjectCreated(created);
			return created;
		} catch (err) {
			set({ lastError: errorMessage(err) });
			return null;
		}
	},

	removeProject: async (id) => {
		try {
			await LisRpcClient.instance().deleteProject(id);
			get()._applyProjectDeleted(id);
		} catch (err) {
			set({ lastError: errorMessage(err) });
		}
	},

	updateProjectMeta: async (id, meta) => {
		try {
			const body: Record<string, unknown> = {};
			if (meta.label            !== undefined) body.label             = meta.label;
			if (meta.color            !== undefined) body.color             = meta.color;
			if (meta.icon             !== undefined) body.icon              = meta.icon;
			if (meta.iconBackground   !== undefined) body.icon_background   = meta.iconBackground;
			if (meta.sidebarCollapsed !== undefined) body.sidebar_collapsed = meta.sidebarCollapsed;

			const updated = await LisRpcClient.instance().updateProject(id, body) as ProjectEntry;
			get()._applyProjectUpdated(updated);
		} catch (err) {
			set({ lastError: errorMessage(err) });
		}
	},

	reorderProjects: async (fromIndex, toIndex) => {
		const current = get().projects;
		if (fromIndex === toIndex || fromIndex < 0 || fromIndex >= current.length || toIndex < 0 || toIndex >= current.length) return;
		const next = [...current];
		const [moved] = next.splice(fromIndex, 1);
		next.splice(toIndex, 0, moved);

		// Optimistic: re-index locally first, then post the new order to the server.
		const reindexed = next.map((p, i) => ({ ...p, sort_index: i }));
		set({ projects: reindexed });

		try {
			await LisRpcClient.instance().reorderProjects(reindexed.map(p => p.id));
		} catch (err) {
			set({ lastError: errorMessage(err) });
			// On failure, rollback by reloading. The SSE flood from a successful PATCH would
			// also fix us up, but explicit reload makes the failure mode deterministic.
			void get().loadAll();
		}
	},

	touchProject: async (id) => {
		try { await LisRpcClient.instance().touchProject(id); }
		catch (err) { set({ lastError: errorMessage(err) }); }
	},

	uploadProjectIcon: async (id, file, source = 'custom') => {
		try {
			const blob = await file.arrayBuffer();
			const base64 = bytesToBase64(new Uint8Array(blob));
			const updated = await LisRpcClient.instance().setProjectIcon(id, {
				mime:         file.type || 'application/octet-stream',
				blob_base64:  base64,
				source,
			}) as ProjectEntry;
			get()._applyProjectUpdated(updated);
			return { ok: true };
		} catch (err) {
			return { ok: false, error: errorMessage(err) };
		}
	},

	removeProjectIcon: async (id) => {
		try {
			const updated = await LisRpcClient.instance().removeProjectIcon(id) as ProjectEntry;
			get()._applyProjectUpdated(updated);
			return { ok: true };
		} catch (err) {
			return { ok: false, error: errorMessage(err) };
		}
	},

	_setFromServer: (projects) => set({ projects: sortByIndex(projects) }),

	_applyProjectCreated: (project) => {
		const without = get().projects.filter(p => p.id !== project.id);
		set({ projects: sortByIndex([...without, project]) });
	},

	_applyProjectUpdated: (project) => {
		const next = get().projects.map(p => p.id === project.id ? project : p);
		// If the updated id isn't already in the list (rare race), add it.
		const exists = next.some(p => p.id === project.id);
		set({ projects: sortByIndex(exists ? next : [...next, project]) });
	},

	_applyProjectDeleted: (id) => {
		set({
			projects:        get().projects.filter(p => p.id !== id),
			activeProjectId: get().activeProjectId === id ? null : get().activeProjectId,
		});
	},
}));

/**
 * Wires the store to the {@link LisRpcClient} SSE feed AND triggers the
 * initial REST load as soon as config arrives. Call this once near webview
 * boot, after `LisRpcClient.install`. Returns a combined unsubscribe.
 *
 * Loading is gated on `onConfigure` so the first fetch doesn't race against
 * the host's `lis.init` postMessage (the bug fixed for useGlobalSessionsStore
 * in eb03cf3 — same shape applies here).
 */
export function installProjectsSync(): () => void {
	const client = LisRpcClient.instance();
	const store = useProjectsStore.getState();

	// Bootstrap the initial REST load every time config is set (initial + future
	// `lis.config_update`). The store's own loading flag de-dups concurrent calls.
	const unsubConfigure = client.onConfigure(() => void store.loadAll());

	const unsubSse = client.onSse((ev) => {
		switch (ev.event) {
			case 'project_created':
				if (isProjectPayload(ev.data)) store._applyProjectCreated(ev.data.project);
				return;
			case 'project_updated':
				if (isProjectPayload(ev.data)) store._applyProjectUpdated(ev.data.project);
				return;
			case 'project_deleted':
				if (isDeletePayload(ev.data)) store._applyProjectDeleted(ev.data.project_id);
				return;
			default:
				return;
		}
	});
	return () => { unsubConfigure(); unsubSse(); };
}

function isProjectPayload(data: unknown): data is { project: ProjectEntry } {
	return !!data && typeof data === 'object' && 'project' in data && !!(data as { project?: unknown }).project;
}

function isDeletePayload(data: unknown): data is { project_id: string } {
	return !!data && typeof data === 'object' && 'project_id' in data;
}

function bytesToBase64(bytes: Uint8Array): string {
	let binary = '';
	const chunk = 0x8000;
	for (let i = 0; i < bytes.length; i += chunk) {
		binary += String.fromCharCode(...bytes.subarray(i, i + chunk));
	}
	return btoa(binary);
}

function errorMessage(err: unknown): string {
	if (err instanceof LisRpcError) return err.message;
	if (err instanceof Error)        return err.message;
	return String(err);
}
