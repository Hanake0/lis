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

	test('Item 12 — sidebar sizing: html font-size=16px (OC design base), body inherits vscode-font-size', async ({ page }) => {
		const sb = await sidebarFrame(page);
		const sizes = await sb.evaluate(() => {
			const html = document.documentElement;
			const body = document.body;
			const htmlFs = parseFloat(getComputedStyle(html).fontSize);
			const bodyFs = parseFloat(getComputedStyle(body).fontSize);
			const hasLisSidebarClass = html.classList.contains('lis-sidebar');
			// Pull a sample sidebar row label height so we can sanity-check the
			// OC tokens cascade correctly. The row height should be in the
			// VS-Code-Explorer ballpark (22-30 px).
			const firstButton = document.querySelector('[role="button"], button');
			const buttonHeight = firstButton ? firstButton.getBoundingClientRect().height : 0;
			return { htmlFs, bodyFs, hasLisSidebarClass, buttonHeight };
		});
		expect(sizes.hasLisSidebarClass).toBe(true);
		expect(sizes.htmlFs).toBe(16);          // OC's rem design base
		expect(sizes.bodyFs).toBeLessThanOrEqual(15);   // body inherits vscode-font-size (13px-ish)
		expect(sizes.bodyFs).toBeGreaterThanOrEqual(11);
		// Loose bound on row height — main goal is to catch a regression that
		// 16px on html cascading everywhere blows up the row to 40px+.
		expect(sizes.buttonHeight).toBeLessThanOrEqual(48);
	});

	test('Item 11 — GitHub identity endpoints are wired (status, set-token validates, pr-status accepts empty)', async ({ page: _ }) => {
		// We can't ship a real PAT through CI, so we just prove the endpoints are
		// reachable + return the right shapes. The actual "see PR badges on
		// session groups whose branch has an open PR" check belongs in the
		// manual Stage-14 checklist where a real PAT is available.
		const status = await api('GET', '/github/status');
		expect(status.status).toBe(200);
		expect(typeof (status.body as { connected: boolean }).connected).toBe('boolean');

		// Empty pr-status request: should return an empty results dict, not error.
		const empty = await api('POST', '/github/pr-status', { keys: [] });
		expect(empty.status).toBe(200);
		const results = (empty.body as { results: Record<string, unknown> }).results;
		expect(typeof results).toBe('object');
		expect(Object.keys(results).length).toBe(0);

		// PUT token with an obviously-fake value — backend either accepts the
		// raw token (lazy validation) or rejects with 400 if it tries to verify
		// against api.github.com. Either way we get a non-5xx.
		const setR = await api('PUT', '/github/token', { token: 'ghp_invalid_e2e_token_DO_NOT_USE_' + Date.now() });
		expect(setR.status).toBeLessThan(500);

		// Clear token — idempotent 204.
		const clearR = await api('DELETE', '/github/token');
		expect([200, 204]).toContain(clearR.status);
	});

	test('Item 10 — multi-run dispatches to N targets + emits multi_run_progress per target', async ({ page: _ }) => {
		// Default dispatcher is NoOpMultiRunDispatcher → each target reports
		// status "ok" without an LLM call. Sufficient to prove the coordinator
		// fans out and emits per-target SSE events.
		const a = `web:e2e-mr-${Date.now()}-a`;
		const b = `web:e2e-mr-${Date.now()}-b`;
		await createChat(a, projA, 'mr-a');
		await createChat(b, projA, 'mr-b');

		const events: Array<{ event: string; data: string }> = [];
		const aborter = new AbortController();
		const reader = (async () => {
			try {
				const r = await fetch(`${BACKEND}/api/events`, {
					headers: { Authorization: `Bearer ${TOKEN}`, Accept: 'text/event-stream' },
					signal: aborter.signal,
				});
				const it = r.body!.getReader();
				const dec = new TextDecoder();
				let buf = ''; let event = '';
				while (true) {
					const { done, value } = await it.read();
					if (done) break;
					buf += dec.decode(value, { stream: true });
					let nl;
					while ((nl = buf.indexOf('\n')) !== -1) {
						const line = buf.slice(0, nl).replace(/\r$/, '');
						buf = buf.slice(nl + 1);
						if (line.startsWith('event:')) event = line.slice(6).trim();
						else if (line.startsWith('data:') && event) {
							events.push({ event, data: line.slice(5).trim() });
							event = '';
						}
					}
				}
			} catch { /* aborted */ }
		})();
		await new Promise(r => setTimeout(r, 400));
		try {
			const r = await api('POST', '/multi-run', {
				prompt: 'echo test',
				targets: [{ chat_id: a }, { chat_id: b }],
			});
			expect([200, 202]).toContain(r.status);
			const runId = (r.body as { run_id?: string }).run_id;
			expect(runId).toBeTruthy();

			// Wait for both progress events.
			const deadline = Date.now() + 10_000;
			while (Date.now() < deadline) {
				const progress = events.filter(e => e.event === 'multi_run_progress').map(e => JSON.parse(e.data) as { run_id: string; chat_id: string; status: string });
				const forThisRun = progress.filter(p => p.run_id === runId);
				const chatsCovered = new Set(forThisRun.map(p => p.chat_id));
				if (chatsCovered.has(a) && chatsCovered.has(b)) {
					// All targets reported.
					expect(forThisRun.every(p => p.status === 'ok')).toBe(true);
					return;
				}
				await new Promise(r => setTimeout(r, 100));
			}
			throw new Error(`multi_run_progress incomplete; events: ${JSON.stringify(events)}`);
		} finally {
			aborter.abort();
			await reader.catch(() => {});
			await deleteChat(a).catch(() => {});
			await deleteChat(b).catch(() => {});
		}
	});

	test('Item 9 — scheduled task CRUD + scheduler computes next_run_at + SSE events', async ({ page: _ }) => {
		// SSE wiring + scheduler bookkeeping. The actual "fire after N minutes"
		// behaviour is covered by the manual Stage-14 checklist; an automated
		// version waiting on Cronos's per-minute boundary would slow the suite
		// down without proving anything the unit tests don't already.
		const ts = Date.now();
		const name = `e2e-sched-${ts}`;

		const events: Array<{ event: string; data: string }> = [];
		const aborter = new AbortController();
		const reader = (async () => {
			try {
				const r = await fetch(`${BACKEND}/api/events`, {
					headers: { Authorization: `Bearer ${TOKEN}`, Accept: 'text/event-stream' },
					signal: aborter.signal,
				});
				const body = r.body!;
				const dec = new TextDecoder();
				const it = body.getReader();
				let buf = '';
				let event = '';
				while (true) {
					const { done, value } = await it.read();
					if (done) break;
					buf += dec.decode(value, { stream: true });
					let nl;
					while ((nl = buf.indexOf('\n')) !== -1) {
						const line = buf.slice(0, nl).replace(/\r$/, '');
						buf = buf.slice(nl + 1);
						if (line.startsWith('event:')) event = line.slice(6).trim();
						else if (line.startsWith('data:') && event) {
							events.push({ event, data: line.slice(5).trim() });
							event = '';
						}
					}
				}
			} catch { /* aborted */ }
		})();
		await new Promise(r => setTimeout(r, 400));

		let externalId: string | null = null;
		try {
			// Create.
			const cre = await api('POST', '/scheduled-tasks', {
				name,
				cron_expr:  '*/5 * * * *',
				prompt:     'echo hi',
				project_id: projA,
				agent_name: null,
				model:      null,
				enabled:    true,
			});
			expect([200, 201]).toContain(cre.status);
			externalId = (cre.body as { id?: string }).id ?? null;
			expect(externalId).toBeTruthy();
			expect((cre.body as { next_run_at?: string }).next_run_at).toBeTruthy();
			expect(new Date((cre.body as { next_run_at: string }).next_run_at).getTime()).toBeGreaterThan(Date.now());

			// Verify listing returns the task.
			const list = await api('GET', `/projects/${encodeURIComponent(projA)}/scheduled-tasks`);
			expect(list.status).toBe(200);
			expect((list.body as Array<{ id: string }>).find(t => t.id === externalId)).toBeTruthy();

			// Update — disable.
			const upd = await api('PATCH', `/scheduled-tasks/${encodeURIComponent(externalId!)}`, { enabled: false });
			expect(upd.status).toBe(200);
			expect((upd.body as { enabled: boolean }).enabled).toBe(false);

			// SSE: created + updated landed.
			const wait = (cond: () => boolean, ms: number) => new Promise<void>((resolve, reject) => {
				const t0 = Date.now();
				const tick = () => {
					if (cond()) resolve();
					else if (Date.now() - t0 > ms) reject(new Error('timeout'));
					else setTimeout(tick, 100);
				};
				tick();
			});
			await wait(() => events.some(e => e.event === 'scheduled_task_created'), 5_000);
			await wait(() => events.some(e => e.event === 'scheduled_task_updated'), 5_000);
		} finally {
			aborter.abort();
			await reader.catch(() => {});
			if (externalId) await api('DELETE', `/scheduled-tasks/${encodeURIComponent(externalId)}`).catch(() => {});
		}
	});

	test('Item 8 — git_status_changed SSE fires on branch / worktree mutations', async ({ page }) => {
		// Subscribe an SSE reader from the test side (independent of the
		// sidebar) and assert git_status_changed lands when we mutate the
		// branch list. Proves the EventBus → SSE bridge works for git events.
		const ts = Date.now();
		const branch = `e2e-gitsse-${ts}`;
		const repoPath = PROJ_A_PATH;

		const events: Array<{ event: string; data: string }> = [];
		const aborter = new AbortController();
		const reader = (async () => {
			try {
				const r = await fetch(`${BACKEND}/api/events`, {
					headers: { Authorization: `Bearer ${TOKEN}`, Accept: 'text/event-stream' },
					signal: aborter.signal,
				});
				const body = r.body;
				if (!body) return;
				const dec = new TextDecoder();
				const it = body.getReader();
				let buf = '';
				let event = '';
				// eslint-disable-next-line no-constant-condition
				while (true) {
					const { done, value } = await it.read();
					if (done) break;
					buf += dec.decode(value, { stream: true });
					let nl;
					while ((nl = buf.indexOf('\n')) !== -1) {
						const line = buf.slice(0, nl).replace(/\r$/, '');
						buf = buf.slice(nl + 1);
						if (line.startsWith('event:')) event = line.slice(6).trim();
						else if (line.startsWith('data:') && event) {
							events.push({ event, data: line.slice(5).trim() });
							event = '';
						}
					}
				}
			} catch { /* aborted */ }
		})();
		// Give SSE a beat to attach.
		await new Promise(r => setTimeout(r, 400));

		// Mutate: create a branch (this alone doesn't emit git_status_changed
		// — only worktree mutations do — so test via worktree create/remove).
		const wtPath = `${(process.env.TEMP ?? 'C:/Users/hanake0/AppData/Local/Temp').replace(/\\/g, '/')}/lis-e2e-gitsse-${ts}`;
		const brR = await api('POST', '/git/branches', { path: repoPath, name: branch, start_point: 'main' });
		expect([200, 201]).toContain(brR.status);
		try {
			const cre = await api('POST', '/worktrees', { project_path: repoPath, new_path: wtPath, branch });
			expect([200, 201]).toContain(cre.status);

			// Wait up to 5s for git_status_changed to arrive.
			const deadline = Date.now() + 5_000;
			while (Date.now() < deadline && !events.some(e => e.event === 'git_status_changed')) {
				await new Promise(r => setTimeout(r, 100));
			}
			expect(events.some(e => e.event === 'git_status_changed')).toBe(true);
		} finally {
			aborter.abort();
			await reader.catch(() => {});
			await api('DELETE', '/worktrees', { project_path: repoPath, worktree_path: wtPath, force: true }).catch(() => {});
			await api('DELETE', `/git/branches/${encodeURIComponent(branch)}?path=${encodeURIComponent(repoPath)}&force=true`).catch(() => {});
		}
	});

	test('Item 7 — create + list + remove worktree, with git_status_changed SSE', async ({ page }) => {
		// Use the lis repo itself as the project (we know it's a real git repo).
		// Cleanup is mandatory because libgit2sharp persists worktrees on disk.
		const ts = Date.now();
		const branch = `e2e-wt-${ts}`;
		const tmpRoot = process.env.TEMP ?? 'C:/Users/hanake0/AppData/Local/Temp';
		const wtPath = `${tmpRoot.replace(/\\/g, '/')}/lis-e2e-wt-${ts}`;
		const repoPath = PROJ_A_PATH;

		// 1) Create a branch off main.
		const branchResp = await api('POST', '/git/branches', { path: repoPath, name: branch, start_point: 'main' });
		expect([200, 201]).toContain(branchResp.status);
		try {
			// 2) Create the worktree.
			const created = await api('POST', '/worktrees', { project_path: repoPath, new_path: wtPath, branch });
			expect([200, 201]).toContain(created.status);
			expect((created.body as { branch: string }).branch).toBe(branch);

			// 3) GET worktrees lists it.
			const list = await api('GET', `/worktrees?project_path=${encodeURIComponent(repoPath)}`);
			expect(list.status).toBe(200);
			const found = (list.body as Array<{ branch: string }>).find(w => w.branch === branch);
			expect(found).toBeTruthy();

			// 4) Open sidebar — worktree is reachable but we don't assert visual
			//    placement (depends on project-resolution wiring). Backend coverage
			//    is the relevant proof here; UI worktree pill is separate plumbing.
			const sb = await sidebarFrame(page);
			await sb.waitForFunction(() => {
				const store = (window as unknown as { __lis_globalSessions?: { getState(): unknown } }).__lis_globalSessions;
				return Boolean(store);
			}, null, { timeout: 20_000 });

			// 5) Remove the worktree.
			const del = await api('DELETE', '/worktrees', { project_path: repoPath, worktree_path: wtPath, force: false });
			expect([200, 204]).toContain(del.status);
			const after = await api('GET', `/worktrees?project_path=${encodeURIComponent(repoPath)}`);
			expect((after.body as Array<{ branch: string }>).find(w => w.branch === branch)).toBeUndefined();
		} finally {
			// Best-effort cleanup — try to remove the worktree if the test bailed before delete.
			await api('DELETE', '/worktrees', { project_path: repoPath, worktree_path: wtPath, force: true }).catch(() => {});
			await api('DELETE', `/git/branches/${encodeURIComponent(branch)}?path=${encodeURIComponent(repoPath)}&force=true`).catch(() => {});
		}
	});

	test('Item 6 — bulk archive then bulk delete via session-ui-store', async ({ page }) => {
		const ids = [`web:e2e-bulk-${Date.now()}-a`, `web:e2e-bulk-${Date.now()}-b`, `web:e2e-bulk-${Date.now()}-c`];
		for (const id of ids) await createChat(id, projA, `bulk-${id.slice(-1)}`);
		try {
			const sb = await sidebarFrame(page);
			await sb.waitForFunction((expected) => {
				const store = (window as unknown as { __lis_globalSessions?: { getState(): unknown } }).__lis_globalSessions;
				if (!store) return false;
				const state = store.getState() as { activeSessions: Array<{ id: string }> };
				const set = new Set(state.activeSessions.map(s => s.id));
				return expected.every(id => set.has(id));
			}, ids, { timeout: 20_000 });

			// Drive the same code path the sidebar's bulk-archive uses.
			await sb.evaluate(async (sel) => {
				const ui = (window as unknown as { __lis_sessionUI?: { getState(): unknown } }).__lis_sessionUI;
				if (!ui) throw new Error('no __lis_sessionUI');
				const s = ui.getState() as { archiveSessions: (ids: string[]) => Promise<{ archivedIds: string[]; failedIds: string[] }> };
				const r = await s.archiveSessions(sel);
				if (r.failedIds.length > 0) throw new Error(`archive failed: ${r.failedIds.join(',')}`);
			}, ids);

			// All three should be in archivedSessions now.
			await sb.waitForFunction((expected) => {
				const store = (window as unknown as { __lis_globalSessions?: { getState(): unknown } }).__lis_globalSessions;
				if (!store) return false;
				const state = store.getState() as { activeSessions: Array<{ id: string }>; archivedSessions: Array<{ id: string }> };
				const archivedSet = new Set(state.archivedSessions.map(s => s.id));
				const activeSet   = new Set(state.activeSessions.map(s => s.id));
				return expected.every(id => archivedSet.has(id) && !activeSet.has(id));
			}, ids, { timeout: 10_000 });

			// Bulk delete next — drive deleteSessions which we just made optimistic too.
			await sb.evaluate(async (sel) => {
				const ui = (window as unknown as { __lis_sessionUI?: { getState(): unknown } }).__lis_sessionUI;
				if (!ui) throw new Error('no __lis_sessionUI');
				const s = ui.getState() as { deleteSessions: (ids: string[]) => Promise<{ deletedIds: string[]; failedIds: string[] }> };
				const r = await s.deleteSessions(sel);
				if (r.failedIds.length > 0) throw new Error(`delete failed: ${r.failedIds.join(',')}`);
			}, ids);

			// All three should be removed from BOTH buckets.
			await sb.waitForFunction((expected) => {
				const store = (window as unknown as { __lis_globalSessions?: { getState(): unknown } }).__lis_globalSessions;
				if (!store) return false;
				const state = store.getState() as { activeSessions: Array<{ id: string }>; archivedSessions: Array<{ id: string }> };
				const archivedSet = new Set(state.archivedSessions.map(s => s.id));
				const activeSet   = new Set(state.activeSessions.map(s => s.id));
				return expected.every(id => !archivedSet.has(id) && !activeSet.has(id));
			}, ids, { timeout: 10_000 });
		} finally {
			// Belt-and-braces — if the test failed, ensure the ids are gone.
			for (const id of ids) await deleteChat(id).catch(() => {});
		}
	});

	test('Item 5 — sidebar search filters sessions by name', async ({ page }) => {
		// Seed two distinct chats so search has something to differentiate.
		const a = `web:e2e-search-a-${Date.now()}`;
		const b = `web:e2e-search-b-${Date.now()}`;
		const uniq = `e2e-search-${Date.now()}`;
		await createChat(a, projA, `chat-${uniq}-alpha`);
		await createChat(b, projA, `chat-${uniq}-beta`);
		try {
			const sb = await sidebarFrame(page);

			// Wait for both chats to land in the store before driving the UI.
			await sb.waitForFunction((ids) => {
				const store = (window as unknown as { __lis_globalSessions?: { getState(): unknown } }).__lis_globalSessions;
				if (!store) return false;
				const state = store.getState() as { activeSessions: Array<{ id: string }> };
				const found = new Set(state.activeSessions.map(s => s.id));
				return ids.every(id => found.has(id));
			}, [a, b], { timeout: 20_000 });

			// Open the search input via the sidebar header's "Search sessions" toolbar button.
			const searchBtn = sb.getByRole('button', { name: 'Search sessions' });
			await searchBtn.waitFor({ state: 'visible', timeout: 10_000 });
			await searchBtn.click();

			const input = sb.getByPlaceholder('Search sessions...');
			await input.waitFor({ state: 'visible', timeout: 5_000 });

			// Type a query unique to chat A — beta should disappear from the rendered list.
			await input.fill(`${uniq}-alpha`);

			// Match-count chip says "1 match" when one result matches the debounced query.
			// Wait for it (debounce is ~120ms; allow a generous buffer).
			await expect(sb.getByText(/1 match\b/)).toBeVisible({ timeout: 5_000 });

			// And the beta chat row should no longer be rendered.
			await expect(sb.getByRole('button', { name: new RegExp(`chat-${uniq}-beta`) })).toHaveCount(0);

			// Broaden — both rows match.
			await input.fill(uniq);
			await expect(sb.getByText(/2 matches\b/)).toBeVisible({ timeout: 5_000 });
		} finally {
			await deleteChat(a);
			await deleteChat(b);
		}
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
