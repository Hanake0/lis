import { chromium, type FullConfig } from '@playwright/test';
import { existsSync, mkdirSync } from 'fs';
import { dirname }                from 'path';

/**
 * One-shot seed of VS Code Web's per-browser IndexedDB. Real Settings UI,
 * real workbench. Run once → snapshot → every spec inherits.
 *
 * Re-seed by deleting `.playwright/state.json` (e.g. after changing the
 * backend URL the user runs locally).
 */
export default async function globalSetup(config: FullConfig): Promise<void> {
	const stateFile = (config.projects[0]?.use?.storageState as string) ?? '.playwright/state.json';
	const baseURL   = config.projects[0]?.use?.baseURL!;
	const serverUrl = process.env.LIS_SERVER_URL ?? 'http://127.0.0.1:3010';
	const authToken = process.env.LIS_AUTH_TOKEN ?? 'dev';

	if (existsSync(stateFile) && !process.env.LIS_RESEED) {
		console.log(`[global-setup] reusing ${stateFile} (set LIS_RESEED=1 to re-seed)`);
		return;
	}

	mkdirSync(dirname(stateFile), { recursive: true });
	console.log(`[global-setup] seeding lis config — server=${serverUrl} token=${authToken ? 'set' : '∅'}`);

	const browser = await chromium.launch({ headless: false });
	const ctx     = await browser.newContext({ viewport: { width: 1400, height: 900 } });
	const page    = await ctx.newPage();

	try {
		await page.goto(baseURL, { waitUntil: 'domcontentloaded' });

		// Wait for the workbench to mount — Lis activity-bar tab is the
		// cheapest stable signal (the extension manifest registers the
		// container even before the extension activates).
		await page.waitForSelector('[role="tab"][aria-label="Lis"], a[aria-label="Lis"]', {
			timeout: 45_000,
			state:   'attached',
		});
		console.log('[global-setup] workbench mounted');

		// Workspace trust banner / dialog. VS Code Web shows a trust prompt
		// on first folder open ("This folder is not trusted"). Without trust,
		// workspace settings (.vscode/settings.json) are filtered. Click
		// "Trust" if present — silent skip if it isn't.
		await dismissTrustPrompt(page);

		// Open the User Settings (JSON) editor. We try keyboard first
		// (Ctrl+Shift+P → palette → type) and fall back to the gear icon
		// menu in the activity-bar (bottom-left) if that path is blocked.
		await openSettingsJson(page);

		// Replace editor contents with the seed. Monaco editor accepts
		// keyboard input directly when focused — we Tab into it from the
		// last menu interaction. `Control+a` selects everything in the
		// current model.
		const seed = JSON.stringify({
			'lis.serverUrl':                serverUrl,
			'lis.authToken':                authToken,
			'lis.verboseLogging':           true,
			'security.workspace.trust.enabled': false,  // skip re-prompts on subsequent loads
		}, null, 2);

		// Click into the editor area first so keystrokes land on Monaco.
		// Monaco's input textarea is `.inputarea` inside `.monaco-editor`,
		// but matching it requires the editor to be visible + focused. Try
		// a few selectors and fall through to clicking the visible editor
		// body itself.
		await page.waitForTimeout(1200);
		const editorSelectors = [
			'.monaco-editor:not(.monaco-editor-rendered-view-zone) textarea.inputarea',
			'.monaco-editor textarea',
			'.monaco-editor[role="code"]',
			'.monaco-editor .view-lines',
			'.monaco-editor',
		];
		let clicked = false;
		for (const sel of editorSelectors) {
			const el = page.locator(sel).first();
			if (await el.isVisible({ timeout: 2_000 }).catch(() => false)) {
				await el.click({ timeout: 3_000 }).catch(() => undefined);
				clicked = true;
				console.log(`[global-setup] editor focused via ${sel}`);
				break;
			}
		}
		if (!clicked) {
			console.warn('[global-setup] no editor selector matched; sending keystrokes anyway');
		}
		await page.keyboard.press('Control+a');
		await page.keyboard.press('Delete');
		await page.keyboard.insertText(seed);
		await page.waitForTimeout(300);
		await page.keyboard.press('Control+s');
		console.log('[global-setup] settings JSON saved');

		// Reload so the new settings take effect, then verify the status
		// bar reports "Lis connected — <serverUrl>". This proves both
		// settings landed (otherwise isConfigured stays false).
		await page.waitForTimeout(1500);
		await page.goto(baseURL, { waitUntil: 'domcontentloaded' });
		await page.waitForSelector('[aria-label*="Lis connected" i], [aria-label*="Lis disconnected" i]', {
			timeout: 60_000,
			state:   'attached',
		});
		const status = await page.locator('[aria-label*="Lis connected" i], [aria-label*="Lis disconnected" i]')
			.first()
			.getAttribute('aria-label');
		console.log(`[global-setup] status bar reports: ${status}`);
		if (!status || !/Lis connected/i.test(status)) {
			throw new Error(`Settings seed did not take effect. Status bar: "${status}"`);
		}

		// Snapshot everything (IndexedDB + cookies + localStorage). Future
		// runs mount with this state and skip the seeding.
		await ctx.storageState({ path: stateFile, indexedDB: true });
		console.log(`[global-setup] storage state saved → ${stateFile}`);
	} finally {
		await browser.close();
	}
}

async function dismissTrustPrompt(page: any): Promise<void> {
	// VS Code's trust dialog uses aria-label "Trust Folder & Continue" in EN
	// and "Confiar na pasta..." in pt. Match by command id where possible —
	// the Trust button carries data-keybinding-context or a deterministic
	// class. Try a few selectors with a short timeout each.
	const candidates = [
		'button:has-text("Trust Folder")',
		'button:has-text("Trust")',
		'button:has-text("Confiar")',
		'.monaco-button:has-text("Trust")',
		'[aria-label*="trust" i]:not([aria-label*="not"])',
	];
	for (const sel of candidates) {
		const btn = page.locator(sel).first();
		if (await btn.isVisible({ timeout: 1500 }).catch(() => false)) {
			console.log(`[global-setup] trust prompt detected via ${sel}`);
			await btn.click().catch(() => undefined);
			await page.waitForTimeout(500);
			return;
		}
	}
	console.log('[global-setup] no trust prompt (already trusted or auto-dismissed)');
}

async function openSettingsJson(page: any): Promise<void> {
	// Method 1: Command Palette (Ctrl+Shift+P) → "Open User Settings (JSON)".
	// Locale-independent because we use the command id, not the localised
	// label — typing the id matches because VS Code's palette is fuzzy.
	await page.keyboard.press('Control+Shift+KeyP');
	const palette = page.locator('.quick-input-widget input.input').first();
	const visible = await palette.isVisible({ timeout: 5_000 }).catch(() => false);
	if (visible) {
		await palette.fill('Preferences: Open User Settings (JSON)');
		await page.waitForTimeout(400);
		await page.keyboard.press('Enter');
		await page.waitForSelector('.monaco-editor', { timeout: 10_000, state: 'attached' });
		await page.waitForTimeout(800);
		console.log('[global-setup] settings JSON opened via palette');
		return;
	}

	// Method 2: Manage gear (bottom-left activity-bar) → Settings menu item.
	console.log('[global-setup] palette unreachable, trying gear menu');
	await page.click('[aria-label*="Manage" i], [aria-label*="Gerenciar" i]', { timeout: 5_000 });
	await page.waitForTimeout(400);
	const settingsItem = page.locator('[role="menuitem"]:has-text("Settings"), [role="menuitem"]:has-text("Configurações")').first();
	await settingsItem.click({ timeout: 5_000 });
	await page.waitForTimeout(1000);

	// Now we're in the Settings UI editor. Click the "Open Settings (JSON)"
	// button in the editor tab (top-right).
	const jsonBtn = page.locator('[aria-label*="Open Settings (JSON)" i], [aria-label*="Abrir Configurações (JSON)" i]').first();
	await jsonBtn.click({ timeout: 5_000 });
	await page.waitForSelector('.monaco-editor', { timeout: 10_000, state: 'attached' });
	await page.waitForTimeout(800);
	console.log('[global-setup] settings JSON opened via gear menu');
}
