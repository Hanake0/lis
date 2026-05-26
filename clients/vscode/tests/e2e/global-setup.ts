import { chromium, type FullConfig } from '@playwright/test';
import { mkdirSync, existsSync } from 'fs';
import { dirname } from 'path';

/**
 * One-time setup that configures `lis.serverUrl` + `lis.authToken` in VS Code
 * Web's IndexedDB, then persists the browser state so every spec inherits a
 * fully-configured workbench.
 *
 * **Why this exists**: VS Code Web stores user-level settings in per-browser
 * IndexedDB (not in the `--user-data-dir/User/settings.json` file, which is
 * for the SERVER-side machine profile). A fresh Playwright Chromium starts
 * with empty IndexedDB, so the extension reads the package.json defaults
 * (empty `authToken` → `isConfigured = false` → status bar shows
 * `debug-disconnect Lis` and the sidebar's REST calls never fire).
 *
 * The fix: pop open the User Settings JSON editor once at the start of the
 * run, write the values, save, then dump `storageState` (which captures
 * IndexedDB + localStorage + cookies). Every spec then mounts with the
 * extension already configured — no first-run notification, no
 * `Lis disconnected` status, no 404s on sidebar bootstrap.
 *
 * Re-run by deleting `.playwright/state.json`. The values come from env
 * vars so CI can inject a different backend URL without editing this file.
 */
export default async function globalSetup(config: FullConfig): Promise<void> {
	const stateFile  = config.projects[0]?.use?.storageState as string ?? '.playwright/state.json';
	const baseURL    = config.projects[0]?.use?.baseURL ?? '';
	const serverUrl  = process.env.LIS_SERVER_URL  ?? 'https://horn-fails-fleet-backing.trycloudflare.com';
	const authToken  = process.env.LIS_AUTH_TOKEN  ?? 'dev';
	const verbose    = process.env.LIS_VERBOSE     ?? 'true';

	if (existsSync(stateFile)) {
		// Already seeded; tests reuse via `storageState`. Delete the file to
		// re-seed (e.g. when changing server URL).
		console.log(`[global-setup] reusing existing storage state at ${stateFile}`);
		return;
	}

	console.log(`[global-setup] seeding Lis config: server=${serverUrl} token=${authToken ? 'set' : '∅'}`);
	mkdirSync(dirname(stateFile), { recursive: true });

	const browser = await chromium.launch();
	const context = await browser.newContext({ viewport: { width: 1400, height: 900 } });
	const page    = await context.newPage();

	await page.goto(baseURL);
	// Wait for the workbench shell — the "Lis" activity tab is the cheapest
	// signal that the extension manifest has been registered (even before
	// the extension itself activates).
	await page.waitForSelector(
		'[role="tab"][aria-label="Lis"], a[aria-label="Lis"]',
		{ timeout: 30_000, state: 'attached' },
	);

	// Open the user settings JSON editor via the command palette. The
	// `Preferences: Open User Settings (JSON)` command is locale-independent
	// when keyed by its id `workbench.action.openSettingsJson`.
	await page.keyboard.press('F1');
	await page.waitForSelector('input.input[aria-label*="palette" i], input.quick-input-box', { timeout: 5_000, state: 'attached' });
	await page.keyboard.type('>Preferences: Open User Settings (JSON)');
	await page.keyboard.press('Enter');
	// Wait for the JSON editor to mount.
	await page.waitForTimeout(1500);

	// Replace the editor contents with our seed JSON. Select-all + paste
	// is more reliable than typing each line.
	await page.keyboard.press('Control+a');
	const seed = JSON.stringify({
		'lis.serverUrl':       serverUrl,
		'lis.authToken':       authToken,
		'lis.verboseLogging':  verbose === 'true',
	}, null, 2);
	await page.keyboard.insertText(seed);
	await page.keyboard.press('Control+s');
	await page.waitForTimeout(500);

	await context.storageState({ path: stateFile, indexedDB: true });
	console.log(`[global-setup] storage state saved to ${stateFile}`);

	await browser.close();
}
