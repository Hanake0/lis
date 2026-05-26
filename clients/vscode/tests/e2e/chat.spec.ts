import { test, expect, type Page, type Frame } from '@playwright/test';

/**
 * End-to-end tests against the REAL VS Code Web (serve-web) instance with
 * the Lis extension installed and configured. globalSetup seeds the
 * IndexedDB; this spec inherits that state via `use.storageState` so the
 * sidebar comes up already connected.
 *
 * Validates the live bugs the user flagged:
 *   - existing chats render their message history (not empty state)
 *   - composer effort picker is present for reasoning-capable models
 *   - send button enables once text is typed
 *   - send fires POST /api/chats/{id}/messages
 *   - send-then-restart: posted message persists across page reload
 */
const BACKEND = process.env.LIS_SERVER_URL ?? 'http://127.0.0.1:3010';
const TOKEN   = process.env.LIS_AUTH_TOKEN ?? 'dev';

test.describe.serial('Chat panel — real VS Code Web', () => {

	test.beforeEach(async ({ page }) => {
		await page.goto('/?folder=' + encodeURIComponent(process.env.LIS_WORKSPACE ?? 'C:/bkp/Dev/Hanake0/lis'));
		await activateLisSidebar(page);
	});

	test('sidebar lists chats from backend', async ({ page }) => {
		const sb = await sidebarFrame(page);
		// At least one of the seeded chats must show in the list.
		await expect(sb.locator('button').filter({ hasText: /\d+(min|h|d) ago|Yesterday/ }).first())
			.toBeVisible({ timeout: 15_000 });
	});

	test('send fires POST /messages on a fresh chat', async ({ page }) => {
		// Seed a chat directly via REST so we know the id + state.
		const { chatId } = await createSeededChat();
		// Click the chat in the sidebar to open the panel.
		const sb = await sidebarFrame(page);
		await sb.getByRole('button', { name: new RegExp(`(harness-send|fresh-send)`) }).first().click({ timeout: 10_000 });
		await waitForChatPanel(page);

		// Wait for composer to mount + currentModelId to resolve.
		const cf = await chatFrame(page);
		const sendBtn = cf.getByRole('button', { name: 'Send message' });
		const textarea = cf.locator('textarea').first();
		await textarea.waitFor({ state: 'visible', timeout: 15_000 });

		// Type and watch for the POST.
		const postPromise = page.waitForRequest(
			(req) => req.method() === 'POST' && /\/api\/chats\/.+\/messages$/.test(req.url()),
			{ timeout: 10_000 },
		);
		await textarea.fill('e2e ping ' + Date.now());
		await expect(sendBtn).toBeEnabled({ timeout: 5_000 });
		await sendBtn.click();
		const req = await postPromise;
		expect(req.url()).toContain('/api/chats/');
	});

	test('opening an existing chat with history loads messages', async ({ page }) => {
		const { chatId, marker } = await createSeededChat();
		const sb = await sidebarFrame(page);
		await sb.getByRole('button', { name: new RegExp(`(harness-send|fresh-send)`) }).first().click({ timeout: 10_000 });
		await waitForChatPanel(page);

		const cf = await chatFrame(page);
		// Wait for either the user message text to render OR for some
		// non-empty-state content. The OC "Start a new chat" text means
		// history wasn't loaded.
		await expect(cf.getByText(marker, { exact: false })).toBeVisible({ timeout: 15_000 });
		// And NOT in empty state.
		expect(await cf.getByText('Start a new chat').count()).toBe(0);
	});

	test('composer effort picker renders for a reasoning-capable model', async ({ page }) => {
		await createSeededChat();
		const sb = await sidebarFrame(page);
		await sb.getByRole('button', { name: /(harness-send|fresh-send)/ }).first().click({ timeout: 10_000 });
		await waitForChatPanel(page);

		const cf = await chatFrame(page);
		await cf.locator('textarea').first().waitFor({ state: 'visible', timeout: 15_000 });
		// Effort picker shows the current variant label as a button
		// (low/medium/high) next to the model picker.
		const effortBtn = cf.getByRole('button', { name: /^(Low|Medium|High|Max|Minimal)$/i }).first();
		await expect(effortBtn).toBeVisible({ timeout: 10_000 });
	});

	test('send-then-reload roundtrip — message persists across page refresh', async ({ page }) => {
		const { chatId, marker } = await createSeededChat();
		const sb = await sidebarFrame(page);
		await sb.getByRole('button', { name: /(harness-send|fresh-send)/ }).first().click({ timeout: 10_000 });
		await waitForChatPanel(page);

		const cf = await chatFrame(page);
		const ta = cf.locator('textarea').first();
		await ta.waitFor({ state: 'visible', timeout: 15_000 });
		const newMsg = 'reload-roundtrip ' + Date.now();
		await ta.fill(newMsg);
		const postPromise = page.waitForRequest(
			(req) => req.method() === 'POST' && /\/api\/chats\/.+\/messages$/.test(req.url()),
			{ timeout: 10_000 },
		);
		await cf.getByRole('button', { name: 'Send message' }).click();
		await postPromise;
		// Give the backend time to persist.
		await page.waitForTimeout(2_000);

		// Reload the whole VS Code Web page — webview state drops, must
		// re-fetch history.
		await page.reload({ waitUntil: 'domcontentloaded' });
		await activateLisSidebar(page);
		const sb2 = await sidebarFrame(page);
		await sb2.getByRole('button', { name: /(harness-send|fresh-send)/ }).first().click({ timeout: 15_000 });
		await waitForChatPanel(page);
		const cf2 = await chatFrame(page);
		await expect(cf2.getByText(newMsg, { exact: false })).toBeVisible({ timeout: 15_000 });
	});
});

// ── helpers ──────────────────────────────────────────────────────────────────

async function activateLisSidebar(page: Page): Promise<void> {
	await page.waitForSelector('[role="tab"][aria-label="Lis"], a[aria-label="Lis"]', {
		timeout: 30_000, state: 'attached',
	});
	await page.evaluate(() => {
		const tab = document.querySelector('[role="tab"][aria-label="Lis"]') as HTMLElement | null;
		tab?.click();
	});
	await page.waitForFunction(
		() => Array.from(document.querySelectorAll('iframe')).some((f) => f.src?.includes('vscode-cdn.net')),
		null,
		{ timeout: 20_000 },
	);
}

async function findInnerFrame(page: Page, title: string): Promise<Frame> {
	const deadline = Date.now() + 20_000;
	while (Date.now() < deadline) {
		for (const f of page.frames()) {
			try {
				const el = await f.frameElement();
				const t  = await el.getAttribute('title');
				if (t === title) return f;
			} catch { /* detached */ }
		}
		await page.waitForTimeout(150);
	}
	throw new Error(`findInnerFrame: no iframe[title="${title}"] within 20s`);
}

async function sidebarFrame(page: Page): Promise<Frame> { return findInnerFrame(page, 'Sessions'); }
async function chatFrame   (page: Page): Promise<Frame> { return findInnerFrame(page, 'Chat'); }

async function waitForChatPanel(page: Page): Promise<void> {
	await page.waitForFunction(() => document.title.startsWith('Chat'), null, { timeout: 15_000 });
}

interface SeededChat { chatId: string; marker: string }

/**
 * Hermetic seed via direct REST: creates a chat, sends one user message
 * with a known marker, returns both. We give it a name prefix so the spec
 * can pick it out of the sidebar list deterministically.
 */
async function createSeededChat(): Promise<SeededChat> {
	const chatId = `web:e2e-fresh-send-${Date.now()}`;
	const marker = `e2e-seed-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`;
	const headers = { 'Content-Type': 'application/json', 'Authorization': `Bearer ${TOKEN}` };

	const create = await fetch(`${BACKEND}/api/chats`, {
		method: 'POST',
		headers,
		body: JSON.stringify({ chat_id: chatId, name: 'fresh-send' }),
	});
	if (!create.ok) {
		throw new Error(`createSeededChat: create failed ${create.status} ${await create.text()}`);
	}

	const send = await fetch(`${BACKEND}/api/chats/${encodeURIComponent(chatId)}/messages`, {
		method: 'POST',
		headers,
		body: JSON.stringify({
			text:           marker,
			model:          null,
			effort:         null,
			tool_profile:   null,
			thinking_mode:  null,
		}),
	});
	if (!send.ok) {
		throw new Error(`createSeededChat: send failed ${send.status} ${await send.text()}`);
	}
	// Backend persists the user message before kicking off the LLM. A short
	// wait is enough for the row to be queryable.
	await new Promise((r) => setTimeout(r, 800));
	return { chatId, marker };
}
