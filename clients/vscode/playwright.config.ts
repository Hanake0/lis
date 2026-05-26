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
const SERVE_WEB_URL  = process.env.LIS_SERVE_WEB ?? 'http://127.0.0.1:8001';
const WORKSPACE_PATH = process.env.LIS_WORKSPACE ?? 'C:/bkp/Dev/Hanake0/lis';
// Export so specs can `page.goto(WORKSPACE_URL)` instead of `'/'` — `goto('/')`
// would resolve relative to baseURL and drop the `?folder=` query string.
export const WORKSPACE_URL = SERVE_WEB_URL + '/?folder=' + encodeURIComponent(WORKSPACE_PATH);

export default defineConfig({
	testDir:     './tests/e2e',
	timeout:     45_000,
	expect:      { timeout: 5_000 },
	fullyParallel: false,    // single serve-web instance, serial
	workers:     1,
	reporter:    [['list']],
	use: {
		baseURL:     WORKSPACE_URL,
		trace:       'retain-on-failure',
		screenshot:  'only-on-failure',
		video:       'off',
		viewport:    { width: 1400, height: 900 },
	},
	projects: [
		{ name: 'chromium', use: { ...devices['Desktop Chrome'] } },
	],
});
