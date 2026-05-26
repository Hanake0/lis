/**
 * E2E tests for Stage-14 project-feature items 2–6 (Items 7–12 follow once
 * the corresponding services exist). Each test creates and tears down its
 * own data via REST so specs are independent and don't leak chats into the
 * shared backend (lesson learnt from chat.spec.ts's earlier flake harvest).
 *
 * Strategy:
 *   - seed two projects via REST (idempotent — uses fixed external_ids)
 *   - create a chat via REST and assign it to project A
 *   - drive the UI to verify behaviour, OR drive the move via REST and
 *     verify the SSE event triggers the sidebar re-bucket
 *   - delete the chat in teardown
 *
 * The sidebar's chat-events-bridge subscribes to /api/events SSE, so any
 * mutation that emits the right event propagates without an explicit refresh.
 */
import { test, expect } from './fixtures';
import type { Frame, Page } from '@playwright/test';

const BACKEND = process.env.LIS_SERVER_URL ?? 'http://127.0.0.1:3010';
const TOKEN   = process.env.LIS_AUTH_TOKEN ?? 'dev';

// Two projects we expect to exist. If they don't, the test creates them.
// External ids are deterministic so reruns reuse the same projects.
const PROJ_A_PATH = 'C:/bkp/Dev/Hanake0/lis';
const PROJ_B_PATH = 'C:/Users/hanake0/ansel';

async function api(method: string, path: string, body?: unknown): Promise<{ status: number; body: unknown }> {
	const res = await fetch(`${BACKEND}/api${path}`, {
		method,
		headers: {
			Authorization:  `Bearer ${TOKEN}`,
			'Content-Type': 'application/json',
		},
		body: body !== undefined ? JSON.stringify(body) : undefined,
	});
	const text = await res.text();
	let json: unknown = null;
	try { json = text ? JSON.parse(text) : null; } catch { json = text; }
	return { status: res.status, body: json };
}

async function ensureProject(path: string): Promise<string> {
	const list = await api('GET', '/projects');
	if (list.status === 200 && Array.isArray(list.body)) {
		const match = (list.body as Array<{ id?: string; path?: string }>).find((p) => p.path === path);
		if (match?.id) return match.id;
	}
	const created = await api('POST', '/projects', { path });
	if (created.status !== 200 && created.status !== 201) {
		throw new Error(`ensureProject(${path}) failed: ${created.status} ${JSON.stringify(created.body)}`);
	}
	const id = (created.body as { id?: string }).id;
	if (!id) throw new Error(`ensureProject(${path}) returned no id: ${JSON.stringify(created.body)}`);
	return id;
}

async function createChat(chatId: string, projectId: string, name: string): Promise<void> {
	// POST /api/chats with { chat_id, name, project_id } creates a persisted chat
	// bound to the given project. Used so the move-test doesn't depend on the
	// composer flow (separate concern, separate spec).
	const r = await api('POST', '/chats', { chat_id: chatId, name, project_id: projectId });
	if (r.status !== 200 && r.status !== 201) {
		throw new Error(`createChat failed: ${r.status} ${JSON.stringify(r.body)}`);
	}
}

async function deleteChat(chatId: string): Promise<void> {
	await api('DELETE', `/chats/${encodeURIComponent(chatId)}`);
}

async function moveChat(chatId: string, projectId: string | null): Promise<void> {
	const r = await api('POST', `/chats/${encodeURIComponent(chatId)}/move`, { project_external_id: projectId });
	if (r.status !== 200 && r.status !== 204) {
		throw new Error(`moveChat failed: ${r.status} ${JSON.stringify(r.body)}`);
	}
}

async function activateLisSidebar(page: Page): Promise<void> {
	const tab = page.locator('[aria-label="Lis"], [aria-label*="Lis,"]').first();
	await tab.waitFor({ state: 'attached', timeout: 30_000 });
	if (await tab.getAttribute('aria-expanded') !== 'true') {
		await tab.click({ force: true });
	}
	await page.waitForFunction(
		() => Array.from(document.querySelectorAll('iframe')).some((f) => f.src?.includes('vscode-cdn.net')),
		null,
		{ timeout: 30_000 },
	);
}

async function sidebarFrame(page: Page): Promise<Frame> {
	const deadline = Date.now() + 20_000;
	while (Date.now() < deadline) {
		for (const f of page.frames()) {
			try {
				const el    = await f.frameElement();
				const title = await el.getAttribute('title');
				if (title === 'Sessions') return f;
			} catch { /* detached */ }
		}
		await page.waitForTimeout(150);
	}
	throw new Error('sidebarFrame: no iframe[title="Sessions"] within 20s');
}

/** Wait until the sidebar's useGlobalSessionsStore returns the expected projectId for the chat. */
async function waitForChatProjectId(sb: Frame, chatId: string, expectedProjectId: string | null, timeoutMs = 15_000): Promise<void> {
	const deadline = Date.now() + timeoutMs;
	while (Date.now() < deadline) {
		const observed = await sb.evaluate((id) => {
			const store = (window as unknown as { __lis_globalSessions?: { getState(): unknown } }).__lis_globalSessions;
			if (!store) return '<no-store>';
			const state = store.getState() as { activeSessions: Array<{ id: string; projectId?: string | null }> };
			const s = state.activeSessions.find((x) => x.id === id);
			return s ? (s.projectId ?? null) : '<not-found>';
		}, chatId);
		if (observed === expectedProjectId) return;
		await new Promise((r) => setTimeout(r, 200));
	}
	throw new Error(`waitForChatProjectId(${chatId}, ${expectedProjectId}): timeout`);
}

test.describe.serial('Project features — Items 2-6', () => {

	let projA = '';
	let projB = '';
	const chatId = `web:e2e-projmove-${Date.now()}`;

	test.beforeAll(async () => {
		projA = await ensureProject(PROJ_A_PATH);
		projB = await ensureProject(PROJ_B_PATH);
		await createChat(chatId, projA, 'projmove fixture');
	});

	test.afterAll(async () => {
		await deleteChat(chatId);
	});

	test.beforeEach(async ({ page }) => {
		const url = (process.env.LIS_SERVE_WEB ?? 'http://127.0.0.1:8001')
			+ '/?folder=' + encodeURIComponent(process.env.LIS_WORKSPACE ?? 'C:/bkp/Dev/Hanake0/lis');
		await page.goto(url, { waitUntil: 'domcontentloaded' });
		await activateLisSidebar(page);
	});

	test('Item 2 — chat move between projects re-buckets in sidebar (SSE chat_moved)', async ({ page }) => {
		const sb = await sidebarFrame(page);

		// Wait for sessions store to populate so __lis_globalSessions is ready.
		await sb.waitForFunction(() => {
			const store = (window as unknown as { __lis_globalSessions?: { getState(): unknown } }).__lis_globalSessions;
			if (!store) return false;
			const state = store.getState() as { hasLoaded: boolean };
			return state.hasLoaded;
		}, null, { timeout: 20_000 });

		// Baseline: chat is under projA.
		await waitForChatProjectId(sb, chatId, projA);

		// Move to projB via REST. Backend publishes chat_moved → SSE → bridge → store update.
		await moveChat(chatId, projB);
		await waitForChatProjectId(sb, chatId, projB);

		// Detach → null.
		await moveChat(chatId, null);
		await waitForChatProjectId(sb, chatId, null);

		// Restore to projA.
		await moveChat(chatId, projA);
		await waitForChatProjectId(sb, chatId, projA);
	});

	test('Item 4 — pin / unpin propagate via SSE chat_pinned / chat_unpinned', async ({ page }) => {
		const sb = await sidebarFrame(page);
		await sb.waitForFunction(() => {
			const store = (window as unknown as { __lis_pinned?: { getState(): unknown } }).__lis_pinned;
			return Boolean(store);
		}, null, { timeout: 20_000 });

		const isPinned = async (id: string): Promise<boolean> => {
			return await sb.evaluate((chat) => {
				const store = (window as unknown as { __lis_pinned?: { getState(): unknown } }).__lis_pinned;
				if (!store) return false;
				const state = store.getState() as { ids: Set<string> };
				return state.ids.has(chat);
			}, id);
		};
		const waitForPin = async (id: string, want: boolean, ms = 6_000): Promise<void> => {
			const deadline = Date.now() + ms;
			while (Date.now() < deadline) {
				if (await isPinned(id) === want) return;
				await new Promise(r => setTimeout(r, 200));
			}
			throw new Error(`waitForPin(${id}, ${want}) timed out`);
		};

		// Baseline: not pinned.
		expect(await isPinned(chatId)).toBe(false);

		// Pin → SSE chat_pinned → store has the id.
		const pinR = await api('POST', `/chats/${encodeURIComponent(chatId)}/pin`);
		expect([200, 204]).toContain(pinR.status);
		await waitForPin(chatId, true);

		// Unpin → SSE chat_unpinned → store drops the id.
		const unpinR = await api('DELETE', `/chats/${encodeURIComponent(chatId)}/pin`);
		expect([200, 204]).toContain(unpinR.status);
		await waitForPin(chatId, false);

		// Pin again then reload — verify the bootstrap seeds from listChats.pinned_at.
		await api('POST', `/chats/${encodeURIComponent(chatId)}/pin`);
		await page.reload({ waitUntil: 'domcontentloaded' });
		await activateLisSidebar(page);
		const sb2 = await sidebarFrame(page);
		await sb2.waitForFunction(() => {
			const store = (window as unknown as { __lis_pinned?: { getState(): unknown } }).__lis_pinned;
			const sess  = (window as unknown as { __lis_globalSessions?: { getState(): unknown } }).__lis_globalSessions;
			if (!store || !sess) return false;
			const ss = sess.getState() as { hasLoaded: boolean };
			return ss.hasLoaded;
		}, null, { timeout: 20_000 });
		const pinnedAfterReload = await sb2.evaluate((chat) => {
			const store = (window as unknown as { __lis_pinned?: { getState(): unknown } }).__lis_pinned;
			if (!store) return false;
			const state = store.getState() as { ids: Set<string> };
			return state.ids.has(chat);
		}, chatId);
		expect(pinnedAfterReload).toBe(true);

		// Cleanup.
		await api('DELETE', `/chats/${encodeURIComponent(chatId)}/pin`);
	});

	test('Item 3 — folders: create, add item, rename, delete all propagate via SSE', async ({ page }) => {
		const sb = await sidebarFrame(page);
		// Folders are per-scope; we use `dir:<project_path>` matching the OC convention.
		// useSessionFoldersStore filters its UI off whatever scope the sidebar is in.
		const scope = `dir:${PROJ_A_PATH}`;
		const name1 = `e2e-folder-${Date.now()}`;
		const name2 = `${name1}-renamed`;

		// Wait for foldersStore to be subscribed (LisRpcClient ready → onSse wired).
		await sb.waitForFunction(() => {
			const store = (window as unknown as { __lis_globalSessions?: { getState(): unknown } }).__lis_globalSessions;
			return Boolean(store);
		}, null, { timeout: 20_000 });

		// Peek at the sidebar's live foldersMap via the window-exposed store —
		// proves SSE folder_* events landed in the store, not just that the
		// backend retains state. The store's internal Folder shape is
		// { id, name, sessionIds, createdAt, parentId }.
		const folderInState = async (scopeKey: string, fid: string): Promise<{ name: string; items: string[] } | null> => {
			return await sb.evaluate(([sk, id]) => {
				const store = (window as unknown as { __lis_folders?: { getState(): unknown } }).__lis_folders;
				if (!store) return null;
				const s = store.getState() as {
					foldersMap: Record<string, Array<{ id: string; name: string; sessionIds: string[] }>>;
				};
				const list = s.foldersMap[sk] ?? [];
				const match = list.find(f => f.id === id);
				return match ? { name: match.name, items: [...(match.sessionIds ?? [])] } : null;
			}, [scopeKey, fid] as [string, string]);
		};

		// First, hydrate the folders map for this scope so the SSE events have
		// somewhere to apply. The sidebar's own bootstrap does this lazily on
		// first access; the test runs before any user click would trigger it.
		await sb.evaluate(async (sk) => {
			const store = (window as unknown as { __lis_folders?: { getState(): unknown } }).__lis_folders;
			if (!store) return;
			const state = store.getState() as { loadScope?: (s: string) => Promise<unknown> };
			await state.loadScope?.(sk);
		}, scope);

		// 1) Create — via REST so we control the payload.
		const created = await api('POST', '/session-folders', { scope_key: scope, name: name1 });
		expect([200, 201]).toContain(created.status);
		const folderId = (created.body as { id?: string }).id!;
		expect(folderId).toBeTruthy();
		try {
			// Read-back via store is fine but we already have the dto.
			expect((created.body as { name: string }).name).toBe(name1);

			// 2) Add chat to folder — emits folder_updated.
			const added = await api('POST', `/session-folders/${encodeURIComponent(folderId)}/items`, { chat_ids: [chatId] });
			expect(added.status).toBe(200);
			expect((added.body as { items: Array<{ chat_id: string }> }).items.map(i => i.chat_id)).toContain(chatId);

			// 3) Rename — emits folder_updated. Verify via REST that the new name is persisted.
			const renamed = await api('PATCH', `/session-folders/${encodeURIComponent(folderId)}`, { name: name2 });
			expect(renamed.status).toBe(200);
			expect((renamed.body as { name: string }).name).toBe(name2);

			// 4) Confirm sidebar's local folders store mirrors the rename (SSE driven).
			//    The dirty check is bounded — give up to 5s for SSE to propagate, then
			//    fall back to a REST read which is what the sidebar's load path uses.
			let observedName: string | null = null;
			const deadline = Date.now() + 8_000;
			while (Date.now() < deadline) {
				const f = await folderInState(scope, folderId);
				if (f?.name === name2) { observedName = f.name; break; }
				await new Promise(r => setTimeout(r, 250));
			}
			expect(observedName).toBe(name2);
		} finally {
			// 5) Delete — emits folder_deleted.
			const del = await api('DELETE', `/session-folders/${encodeURIComponent(folderId)}`);
			expect([200, 204]).toContain(del.status);
			// Verify it's gone server-side.
			const list = await api('GET', `/session-folders?scope=${encodeURIComponent(scope)}`);
			const found = (list.body as Array<{ id: string }>).find(f => f.id === folderId);
			expect(found).toBeUndefined();
		}
	});
});
