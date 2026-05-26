import { chromium, type FullConfig }              from '@playwright/test';
import { copyFileSync, existsSync, mkdirSync, readdirSync, rmSync, statSync } from 'fs';
import { resolve }                                from 'path';

/**
 * One-shot seed of a persistent Chromium profile that lives on disk under
 * `.playwright/profile/`. Each spec mounts via `launchPersistentContext`
 * (see fixtures.ts) which inherits the profile's IndexedDB — VS Code Web
 * sees its own writes and a configured Lis extension.
 *
 * Re-seed: delete `.playwright/profile/` or set `LIS_RESEED=1`.
 */
export default async function globalSetup(_config: FullConfig): Promise<void> {
	const profileDir = resolve(process.cwd(), '.playwright', 'profile');
	const serverUrl  = process.env.LIS_SERVER_URL ?? 'http://127.0.0.1:3010';
	const authToken  = process.env.LIS_AUTH_TOKEN ?? 'dev';
	const baseURL    = (process.env.LIS_SERVE_WEB ?? 'http://127.0.0.1:8001')
		+ '/?folder=' + encodeURIComponent(process.env.LIS_WORKSPACE ?? 'C:/bkp/Dev/Hanake0/lis');

	// Sync the freshly-built bundles into the installed extension's `dist/` so
	// `code serve-web` serves what we just compiled. Without this step, the
	// webview iframe loads the stale chat.js that was there when the user first
	// installed the VSIX — every test then hits the old behaviour. See
	// `LIS_VSCODE_WEB_EXT_DIR` to override the install path.
	syncBundleIntoInstalledExtension();

	if (process.env.LIS_RESEED) {
		console.log('[global-setup] LIS_RESEED=1 — wiping profile dir');
		try { rmSync(profileDir, { recursive: true, force: true }); } catch { /* may not exist */ }
	}

	mkdirSync(profileDir, { recursive: true });

	// `userdata-store` entry that proves we've seeded. If it exists, skip.
	// We can't easily peek inside an IDB without launching the browser, so
	// we use a sentinel file alongside the profile.
	const sentinel = resolve(profileDir, '.lis-seeded');
	if (existsSync(sentinel)) {
		console.log('[global-setup] profile already seeded (sentinel found)');
		return;
	}

	console.log(`[global-setup] seeding lis config in ${profileDir} — server=${serverUrl} token=${authToken ? 'set' : '∅'}`);

	const ctx = await chromium.launchPersistentContext(profileDir, {
		headless: true,
		viewport: { width: 1400, height: 900 },
		// Match fixtures.ts so the seeded profile and test runs share
		// the same PNA-disabled environment.
		args: [
			'--disable-features=LocalNetworkAccessChecks,LocalNetworkAccessChecksWarning,BlockInsecurePrivateNetworkRequests,PrivateNetworkAccessSendPreflights',
		],
	});
	const page = ctx.pages()[0] ?? await ctx.newPage();

	try {
		await page.goto(baseURL, { waitUntil: 'domcontentloaded' });

		// Wait for VS Code Web to bootstrap. The Lis activity-bar tab is
		// the cheapest stable signal that the extension manifest has been
		// scanned + registered.
		await page.waitForSelector('[role="tab"][aria-label="Lis"], a[aria-label="Lis"]', {
			timeout: 60_000, state: 'attached',
		});
		console.log('[global-setup] workbench mounted');

		// Write settings.json directly to the workbench's userdata store.
		// Schema: db `vscode-web-db`, store `vscode-userdata-store`,
		// out-of-line string keys → `Uint8Array` values.
		const settingsJson = JSON.stringify({
			'lis.serverUrl':                              serverUrl,
			'lis.authToken':                              authToken,
			'lis.verboseLogging':                         true,
			'security.workspace.trust.enabled':           false,
			// VS Code shows a bottom-right "A git repository was found in the
			// parent folders" toast whenever the workspace is opened — its
			// notification card overlays the chat composer's send button and
			// intercepts pointer events, making Playwright clicks miss the
			// target. Telling Git never to volunteer this prompt keeps the
			// composer reachable.
			'git.openRepositoryInParentFolders':          'never',
			'workbench.startupEditor':                    'none',
			// Suppress the welcome page on startup so each spec starts in a
			// known editor state — without this, the first opened editor tab
			// after the workspace mounts is "Welcome", which makes the
			// chat-tab-title check (`document.title.startsWith('Chat')`)
			// race against the workbench restoration.
		}, null, 2);

		const result = await page.evaluate(async (content: string) => {
			const db: IDBDatabase = await new Promise((res, rej) => {
				const req = indexedDB.open('vscode-web-db');
				req.onsuccess = () => res(req.result);
				req.onerror   = () => rej(req.error);
			});
			if (!Array.from(db.objectStoreNames).includes('vscode-userdata-store')) {
				db.close();
				return { ok: false, reason: 'no-userdata-store' };
			}
			const bytes = new TextEncoder().encode(content);
			await new Promise<void>((res, rej) => {
				const tx = db.transaction('vscode-userdata-store', 'readwrite');
				tx.objectStore('vscode-userdata-store').put(bytes, '/User/settings.json');
				tx.oncomplete = () => res();
				tx.onerror    = () => rej(tx.error);
				tx.onabort    = () => rej(tx.error);
			});
			db.close();
			return { ok: true };
		}, settingsJson);

		console.log(`[global-setup] settings write: ${JSON.stringify(result)}`);
		if (!(result as any).ok) throw new Error(`Settings write failed: ${JSON.stringify(result)}`);

		// Give VS Code Web a moment to ingest the write into its in-memory
		// settings before we close — otherwise the next run might see a
		// stale config until the watcher fires.
		await page.waitForTimeout(2_000);

		// Reload + verify the status bar reports Lis connected. The
		// extension activates `onView:lis.sidebar` — until we click the
		// Lis activity-bar tab, the status bar item isn't created.
		await page.goto(baseURL, { waitUntil: 'domcontentloaded' });
		await page.waitForSelector('[role="tab"][aria-label="Lis"]', { timeout: 60_000, state: 'attached' });
		await page.evaluate(() => {
			const tab = document.querySelector('[role="tab"][aria-label="Lis"]') as HTMLElement | null;
			tab?.click();
		});
		console.log('[global-setup] Lis tab clicked, waiting for status bar');

		let status: string | null = null;
		const deadline = Date.now() + 30_000;
		while (Date.now() < deadline) {
			const labels = await page.evaluate(() =>
				Array.from(document.querySelectorAll('[aria-label]'))
					.map((e) => e.getAttribute('aria-label')!)
					.filter((s) => /Lis (connected|disconnected)/i.test(s)),
			);
			if (labels.length > 0) { status = labels[0]; break; }
			await page.waitForTimeout(500);
		}
		console.log(`[global-setup] status: ${status ?? '<not found>'}`);
		if (!status || !/Lis connected/i.test(status)) {
			throw new Error(`Seed didn't take. Status: "${status ?? '<not found>'}"`);
		}

		// Mark seeded.
		require('fs').writeFileSync(sentinel, new Date().toISOString());
		console.log('[global-setup] sentinel written, profile ready');
	} finally {
		await ctx.close();
	}
}

/**
 * Mirror `clients/vscode/dist/*` into the installed-extension's `dist/` so
 * `code serve-web` serves the freshly-rebuilt webview bundle. Without this
 * step, the iframe loads whatever VSIX bytes were unpacked on the user's
 * first install, and every code change requires a manual re-VSIX dance.
 *
 * Resolves the destination from `LIS_VSCODE_WEB_EXT_DIR` (override) or the
 * default at `../../lis-vscode-web/extensions/lis.lis-vscode-0.1.0/dist`.
 * Silent no-op if the destination doesn't exist (e.g. running locally
 * without a serve-web installation).
 */
function syncBundleIntoInstalledExtension(): void {
	const srcDir  = resolve(process.cwd(), 'dist');
	const dstDir  = process.env.LIS_VSCODE_WEB_EXT_DIR
		?? resolve(process.cwd(), '..', '..', '..', 'lis-vscode-web', 'extensions', 'lis.lis-vscode-0.1.0', 'dist');
	if (!existsSync(dstDir)) {
		console.log(`[global-setup] no installed-extension dist at ${dstDir} — skipping sync`);
		return;
	}
	let copied = 0;
	for (const file of readdirSync(srcDir)) {
		if (!file.endsWith('.js') && !file.endsWith('.css') && !file.endsWith('.map')) continue;
		const src = resolve(srcDir, file);
		const dst = resolve(dstDir, file);
		try {
			if (existsSync(dst) && statSync(dst).mtimeMs >= statSync(src).mtimeMs) continue;
			copyFileSync(src, dst);
			copied++;
		} catch (e) {
			console.warn(`[global-setup] failed to copy ${file}: ${e instanceof Error ? e.message : String(e)}`);
		}
	}
	console.log(`[global-setup] synced ${copied} bundle file(s) into ${dstDir}`);
}
