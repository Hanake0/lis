import { defineConfig, devices } from '@playwright/test';

/**
 * Drives the **already-running** VS Code serve-web instance (default
 * http://127.0.0.1:8001) at the workspace folder this repo lives in.
 *
 * Why no `webServer`: serve-web is a long-lived dev tool the user starts
 * manually with `code serve-web --extensions-dir … --user-data-dir …`. Tests
 * assume it's up and the Lis extension is installed in the configured
 * extensions-dir. The build pipeline copies `dist/chat.js` into that dir,
 * so a fresh `bun run build` is enough between tests — no Playwright restart.
 */
// WORKSPACE_URL lives in `tests/e2e/helpers.ts` (the config file can't export
// it without breaking spec imports — Playwright loads config once and any
// downstream `test.describe()` call from a spec re-imported by config trips
// "test.describe() called in configuration file").
const SERVE_WEB_URL  = process.env.LIS_SERVE_WEB ?? 'http://127.0.0.1:8001';
const WORKSPACE_PATH = process.env.LIS_WORKSPACE ?? 'C:/bkp/Dev/Hanake0/lis';
const WORKSPACE_URL  = SERVE_WEB_URL + '/?folder=' + encodeURIComponent(WORKSPACE_PATH);

export default defineConfig({
	testDir:       './tests/e2e',
	timeout:       45_000,
	expect:        { timeout: 5_000 },
	fullyParallel: false,    // single serve-web instance, serial
	workers:       1,
	reporter:      [['list']],
	// `global-setup` runs once before any spec and seeds VS Code Web's
	// IndexedDB with `lis.serverUrl` + `lis.authToken`. Tests then mount
	// against the saved storageState below — no first-run notification, no
	// `Lis disconnected` status.
	globalSetup:   require.resolve('./tests/e2e/global-setup'),
	use: {
		baseURL:     WORKSPACE_URL,
		// One-shot file produced by global-setup. Delete it to re-seed
		// (e.g. when LIS_SERVER_URL changes).
		storageState: '.playwright/state.json',
		trace:        'retain-on-failure',
		screenshot:   'only-on-failure',
		video:        'off',
		viewport:     { width: 1400, height: 900 },
	},
	projects: [
		{ name: 'chromium', use: { ...devices['Desktop Chrome'] } },
	],
});
