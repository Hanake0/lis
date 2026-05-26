import type { Page, Locator } from '@playwright/test';

/** Computed lazily so env-var overrides take effect at test run time. */
function computeWorkspaceUrl(): string {
	const base = process.env.LIS_SERVE_WEB ?? 'http://127.0.0.1:8001';
	const ws   = process.env.LIS_WORKSPACE ?? 'C:/bkp/Dev/Hanake0/lis';
	return base + '/?folder=' + encodeURIComponent(ws);
}
export const WORKSPACE_URL = computeWorkspaceUrl();

/**
 * VS Code Web wraps every webview in **two** nested iframes:
 *   outer  — `vscode-cdn.net` shell (cross-origin)
 *   inner  — actual extension HTML (also sandboxed)
 *
 * Playwright's `frameLocator` chain crosses origins transparently — but you
 * have to chain BOTH levels. Locating buttons via top-level `page.locator`
 * fails silently because the role-tree stops at the iframe boundary.
 *
 * These helpers wrap that two-level dance so spec files read like sidebar
 * code instead of frame-archaeology.
 */

const OUTER_IFRAME = 'iframe[src*="vscode-cdn.net"]';
const SIDEBAR_INNER_IFRAME = 'iframe[title="Sessions"]';
const CHAT_INNER_IFRAME    = 'iframe[title="Chat"]';

export async function activateLisSidebar(page: Page): Promise<void> {
	// The activity-bar tab carries the Lis container id in its data-resource
	// attribute (`workbench.view.extension.lis-sidebar`). Match by that — the
	// aria-label localises (`Lis` in en, `Lis` in pt) and may be empty until
	// the extension activates, so attribute matching is more robust.
	await page.waitForSelector(
		'[role="tab"][aria-label="Lis"], [data-vscode-context*="lis-sidebar"], a[aria-label="Lis"]',
		{ timeout: 20_000, state: 'attached' },
	);

	// Click via the matching DOM element. Try a few selectors in order.
	const clicked = await page.evaluate(() => {
		const selectors = [
			'[role="tab"][aria-label="Lis"]',
			'a[aria-label="Lis"]',
			'.action-item .action-label[aria-label="Lis"]',
			'[id*="lis-sidebar"][role="tab"]',
		];
		for (const sel of selectors) {
			const el = document.querySelector(sel) as HTMLElement | null;
			if (el) { el.click(); return sel; }
		}
		// Fallback: search every clickable for an inner Lis label
		const tabs = Array.from(document.querySelectorAll('[role="tab"]'));
		for (const t of tabs) {
			const inner = t.querySelector('[aria-label="Lis"], [title="Lis"]') as HTMLElement | null;
			if (inner) { (t as HTMLElement).click(); return 'inner-lis'; }
		}
		return null;
	});
	if (!clicked) throw new Error('activateLisSidebar: no Lis activity-bar entry found');

	// Wait for the sidebar webview iframe (title=Sessions) to be present.
	await page.waitForSelector('iframe[src*="vscode-cdn.net"]', { timeout: 15_000, state: 'attached' });
}

/**
 * Returns the inner webview frame whose `title` attribute equals `innerTitle`
 * (e.g. `Sessions` for sidebar, `Chat` for the chat panel). We descend via
 * `page.frames()` rather than `frameLocator().filter()` because `FrameLocator`
 * doesn't expose a filter API — and chaining by index breaks once there's
 * more than one chat panel open.
 */
async function findInnerFrame(page: Page, innerTitle: string) {
	const deadline = Date.now() + 10_000;
	while (Date.now() < deadline) {
		for (const f of page.frames()) {
			try {
				const el = await f.frameElement();
				const title = await el.getAttribute('title');
				if (title === innerTitle) return f;
			} catch { /* frame detached mid-loop, skip */ }
		}
		await page.waitForTimeout(150);
	}
	throw new Error(`findInnerFrame: no iframe[title="${innerTitle}"] within 10s`);
}

/** Locator for the *inner* sidebar webview document. */
export async function sidebarFrame(page: Page) {
	return findInnerFrame(page, 'Sessions');
}

/** Locator for the chat panel's *inner* webview document. */
export async function chatFrame(page: Page) {
	return findInnerFrame(page, 'Chat');
}

export async function openChatByName(page: Page, name: string): Promise<void> {
	const sb = await sidebarFrame(page);
	// The chat row button label is e.g. `Breno 3h ago`. Match by prefix using
	// a regex so the relative-time suffix doesn't break the selector across runs.
	await sb.getByRole('button', { name: new RegExp(`^${escapeRe(name)} \\d`) }).first().click();
}

export async function waitForChatPanel(page: Page): Promise<void> {
	// Title flips to `Chat - …` once the panel mounts.
	await page.waitForFunction(() => document.title.startsWith('Chat - '), { timeout: 10_000 });
}

/** Click the top-toolbar "New session" button (chat-new icon). */
export async function clickNewSessionToolbarButton(page: Page): Promise<void> {
	const sb = await sidebarFrame(page);
	await sb.getByRole('button', { name: 'New session', exact: true }).first().click();
}

/** Read the composer's send button enabled state. */
export async function sendButtonDisabled(page: Page): Promise<boolean> {
	const cf = await chatFrame(page);
	return await cf.getByRole('button', { name: 'Send message' }).isDisabled();
}

export async function composerTextarea(page: Page): Promise<Locator> {
	const cf = await chatFrame(page);
	return cf.getByRole('textbox', { name: 'Use @ / ! # for helpers' });
}

export async function typeAndSend(page: Page, text: string): Promise<void> {
	const ta = await composerTextarea(page);
	await ta.click();
	await ta.fill(text);
	const cf = await chatFrame(page);
	await cf.getByRole('button', { name: 'Send message' }).click();
}

/** Count user+assistant message bubbles rendered in the chat panel. */
export async function visibleMessageCount(page: Page): Promise<number> {
	const cf = await chatFrame(page);
	const byId = await cf.locator('[data-message-id]').count();
	if (byId > 0) return byId;
	return await cf.locator('.chat-message-column').count();
}

/** Did the chat panel render the "Start a new chat" empty state? */
export async function hasEmptyState(page: Page): Promise<boolean> {
	const cf = await chatFrame(page);
	return (await cf.getByText('Start a new chat').count()) > 0;
}

function escapeRe(s: string): string {
	return s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}
