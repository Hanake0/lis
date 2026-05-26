import { defineConfig, devices } from '@playwright/test';

/**
 * Drives the **already-running** VS Code Web serve-web instance (default
 * http://127.0.0.1:8001) at this workspace. No webview mocking — real
 * extension running in a real browser, talking to the real Lis backend.
 *
 * **Why no webServer block**: serve-web is the user's long-lived dev tool
 * launched with their `--extensions-dir` containing the Lis VSIX. Tests
 * assume it's up; CI will spin its own serve-web before invoking us.
 *
 * **State seeding**: VS Code Web stores user settings in per-browser
 * IndexedDB. A fresh Playwright Chromium starts with empty storage, so
 * `lis.serverUrl` / `lis.authToken` fall back to package.json defaults
 * (empty token → `isConfigured = false` → sidebar dead). The
 * `global-setup` task opens VS Code Web once, drives the Settings UI to
 * write the seed, then `storageState({ indexedDB: true })` snapshots
 * everything. Every spec reuses the snapshot; delete `.playwright/state.json`
 * to re-seed (e.g. after a backend URL change).
 */
const SERVE_WEB_URL  = process.env.LIS_SERVE_WEB ?? 'http://127.0.0.1:8001';
const WORKSPACE_PATH = process.env.LIS_WORKSPACE ?? 'C:/bkp/Dev/Hanake0/lis';
const WORKSPACE_URL  = SERVE_WEB_URL + '/?folder=' + encodeURIComponent(WORKSPACE_PATH);
const STATE_FILE     = '.playwright/state.json';

export default defineConfig({
	testDir:       './tests/e2e',
	timeout:       60_000,
	expect:        { timeout: 8_000 },
	fullyParallel: false,
	workers:       1,
	reporter:      [['list']],
	globalSetup:   require.resolve('./tests/e2e/global-setup'),
	use: {
		baseURL:      WORKSPACE_URL,
		storageState: STATE_FILE,
		trace:        'retain-on-failure',
		screenshot:   'only-on-failure',
		video:        'off',
		viewport:     { width: 1400, height: 900 },
		// VS Code Web loads slowly on first paint; give actions enough room.
		actionTimeout:    15_000,
		navigationTimeout: 30_000,
	},
	projects: [
		{ name: 'chromium', use: { ...devices['Desktop Chrome'] } },
	],
});
