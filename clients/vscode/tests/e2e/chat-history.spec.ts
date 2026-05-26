import { test, expect } from '@playwright/test';
import { WORKSPACE_URL } from '../../playwright.config';
import {
	activateLisSidebar, chatFrame, openChatByName, waitForChatPanel,
	hasEmptyState, visibleMessageCount, sendButtonDisabled, composerTextarea,
} from './helpers';

/**
 * Reproduces the live bugs the user flagged:
 *  - existing chats render empty state instead of history
 *  - send button disabled with no text typed (expected) but stays disabled
 *    even after text is entered for some chats
 *  - effort/thinking pickers missing from composer chrome
 *
 * Run with: `bun playwright test tests/e2e/chat-history.spec.ts`
 * Requires: serve-web up at 127.0.0.1:8001 with the extension installed.
 */
test.describe('Chat panel — history + composer', () => {
	test.beforeEach(async ({ page }) => {
		// Use the full URL (NOT '/') so the `?folder=` query is preserved.
		await page.goto(WORKSPACE_URL);
		await activateLisSidebar(page);
	});

	test('opening an existing chat renders its message history', async ({ page }) => {
		// "Breno" is the canonical chat row in the lis project with 1+ user message.
		await openChatByName(page, 'Breno');
		await waitForChatPanel(page);
		// Give the bridge time to fetch + apply history.
		await page.waitForTimeout(2000);

		const empty = await hasEmptyState(page);
		const count = await visibleMessageCount(page);
		expect.soft(empty, 'should NOT show "Start a new chat" empty state').toBe(false);
		expect(count, 'at least one user message bubble should render').toBeGreaterThan(0);
	});

	test('composer effort picker is present for a reasoning-capable model', async ({ page }) => {
		await openChatByName(page, 'Breno');
		await waitForChatPanel(page);
		await page.waitForTimeout(1500);

		const composer = await chatFrame(page);
		const lowChip    = await composer.getByRole('button', { name: /^(Low|Medium|High)$/i }).count();
		const effortBtn  = await composer.locator('[aria-label*="thinking" i]').count();
		expect.soft(lowChip + effortBtn, 'effort picker chip must render').toBeGreaterThan(0);
	});

	test('send button enables once text is typed', async ({ page }) => {
		await openChatByName(page, 'Breno');
		await waitForChatPanel(page);
		await page.waitForTimeout(1500);

		expect(await sendButtonDisabled(page), 'empty composer → send disabled').toBe(true);

		const ta = await composerTextarea(page);
		await ta.fill('ping');
		await page.waitForTimeout(200);
		expect(await sendButtonDisabled(page), 'with text → send enabled').toBe(false);
	});

	test('sending a message fires POST /api/chats/{id}/messages', async ({ page }) => {
		await openChatByName(page, 'Breno');
		await waitForChatPanel(page);
		await page.waitForTimeout(1500);

		const postPromise = page.waitForRequest(
			(req) => req.method() === 'POST' && /\/api\/chats\/.+\/messages$/.test(req.url()),
			{ timeout: 8000 },
		);

		const ta = await composerTextarea(page);
		await ta.fill('ping from e2e');
		await page.waitForTimeout(150);
		const cf = await chatFrame(page);
		await cf.getByRole('button', { name: 'Send message' }).click();

		const req = await postPromise;
		expect(req.url()).toMatch(/\/api\/chats\/web%3A[^/]+\/messages$/);
	});

	/**
	 * The user's exact request: send a message, restart the browser, re-open
	 * the same chat, assert the message we just sent is still there. This
	 * catches both "send didn't actually persist" and "history doesn't load
	 * after a fresh mount".
	 */
	test('send + restart: posted message persists across browser reload', async ({ page }) => {
		const unique = `e2e-ping-${Date.now()}`;

		await openChatByName(page, 'Breno');
		await waitForChatPanel(page);
		await page.waitForTimeout(1500);

		const ta = await composerTextarea(page);
		await ta.fill(unique);
		const cf = await chatFrame(page);
		const postPromise = page.waitForRequest(
			(req) => req.method() === 'POST' && /\/api\/chats\/.+\/messages$/.test(req.url()),
			{ timeout: 8000 },
		);
		await cf.getByRole('button', { name: 'Send message' }).click();
		await postPromise;
		// Give the backend time to persist the row before we restart.
		await page.waitForTimeout(1500);

		// Restart: fully reload the page (drops every webview state) and
		// re-open the chat. Bridge's loadHistory should bring our message
		// back from the DB.
		await page.goto(WORKSPACE_URL);
		await activateLisSidebar(page);
		await openChatByName(page, 'Breno');
		await waitForChatPanel(page);
		await page.waitForTimeout(2000);

		const cf2 = await chatFrame(page);
		const present = await cf2.getByText(unique).count();
		expect(present, `posted "${unique}" should reappear after reload`).toBeGreaterThan(0);
	});
});
