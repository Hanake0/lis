/**
 * Custom Playwright fixture that runs every spec inside a
 * `launchPersistentContext` — i.e. a real on-disk Chromium profile that
 * survives between runs. VS Code Web stores user settings in IndexedDB,
 * and Playwright's `storageState({ indexedDB: true })` serialisation +
 * restore turns out to corrupt the workbench state in ways that hide
 * installed extensions. Keeping the profile on disk avoids that whole
 * mess — VS Code reads the same IDB it wrote.
 *
 * Profile dir: `.playwright/profile/` (gitignored). globalSetup seeds it
 * on first run (delete the dir to re-seed).
 */
import { test as base, chromium, type BrowserContext, type Page } from '@playwright/test';
import { mkdirSync }                                              from 'fs';
import { resolve }                                                from 'path';

const PROFILE_DIR = resolve(process.cwd(), '.playwright', 'profile');

mkdirSync(PROFILE_DIR, { recursive: true });

interface Fixtures {
	persistentContext: BrowserContext;
	page:              Page;
}

export const test = base.extend<Fixtures>({
	// Override the default `page` fixture so it comes from a launched
	// persistent context rather than the per-test ephemeral one. Each
	// spec gets a brand-new context (cheap — same profile dir, just a
	// new Chromium process), but the on-disk profile persists.
	page: async ({ }, use) => {
		const ctx = await chromium.launchPersistentContext(PROFILE_DIR, {
			headless: process.env.LIS_HEADED !== '1',
			viewport: { width: 1400, height: 900 },
			// Chromium PNA (Private Network Access) blocks fetches from
			// PUBLIC origins (*.vscode-cdn.net iframe) to PRIVATE
			// loopback addresses (127.0.0.1:3010). The Lis webview lives
			// on the CDN origin and talks to the backend on loopback —
			// disable the block for tests. Production browsers behave the
			// same because the user manually configured serve-web and the
			// backend echoes the PNA header.
			// Chromium's Local Network Access checks block fetches from
			// PUBLIC origins (*.vscode-cdn.net iframe) to PRIVATE loopback
			// addresses (127.0.0.1:3010). Disable for the test browser —
			// the previous-generation feature name is PrivateNetworkAccess,
			// the current is LocalNetworkAccessChecks. Disable both so
			// older + newer Chromium builds both work.
			args: [
				'--disable-features=LocalNetworkAccessChecks,LocalNetworkAccessChecksWarning,BlockInsecurePrivateNetworkRequests,PrivateNetworkAccessSendPreflights',
			],
		});
		const page = ctx.pages()[0] ?? await ctx.newPage();
		await use(page);
		await ctx.close();
	},
	persistentContext: async ({ page }, use) => {
		// Convenience: expose the underlying context for specs that need
		// it (e.g. to spawn a second page).
		await use(page.context());
	},
});

export { expect } from '@playwright/test';
export { PROFILE_DIR };
