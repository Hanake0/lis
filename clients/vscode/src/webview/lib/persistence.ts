/**
 * Local persistence for the webview-side `DesktopSettings` bag. OC's original
 * fans these out to ~1200 LOC of Tauri/Electron + REST-backed settings sync —
 * Lis only persists locally (the backend doesn't expose a settings API yet),
 * so we keep the same write surface but route everything to a single
 * `lis.desktopSettings` JSON blob in localStorage.
 *
 * Why one blob instead of per-key writes (OC's approach)?
 * - Lis stores don't currently read individual OC localStorage keys
 *   (selectedThemeId, lastDirectory, etc.) — they pull from the runtime
 *   stores instead, which are themselves persisted by zustand middleware
 *   where relevant.
 * - Consumers (DefaultsSettings, GitSettings, etc.) call
 *   `updateDesktopSettings({key: value})` purely to remember a preference;
 *   storing the merged bag once is enough for that intent.
 *
 * Two helper writes are kept per-key:
 * - `homeDirectory` → also dropped into `lis.directory.home` so
 *   `useDirectoryStore` picks it up on next boot.
 * - Theme variant + system-theme toggle → also dropped into a top-level
 *   key so `initializeAppearancePreferences` can flip the html.dark class
 *   before React renders.
 */

import type { DesktopSettings } from './desktop';

const SETTINGS_KEY     = 'lis.desktopSettings';
const HOME_KEY         = 'lis.directory.home';
const THEME_VARIANT    = 'lis.theme.variant';
const SYSTEM_THEME     = 'lis.theme.useSystem';

function safeStorage(): Storage | null {
	try { return typeof window !== 'undefined' ? window.localStorage : null; } catch { return null; }
}

function readBlob(): DesktopSettings {
	const storage = safeStorage();
	if (!storage) return {};
	try {
		const raw = storage.getItem(SETTINGS_KEY);
		if (!raw) return {};
		const parsed = JSON.parse(raw);
		return typeof parsed === 'object' && parsed !== null ? parsed as DesktopSettings : {};
	} catch {
		return {};
	}
}

function writeBlob(next: DesktopSettings): void {
	const storage = safeStorage();
	if (!storage) return;
	try { storage.setItem(SETTINGS_KEY, JSON.stringify(next)); } catch { /* swallow quota / serialisation errors */ }
}

/**
 * Snapshot the current persisted bag without applying side effects. Useful for
 * stores that want to recover their last-known state on boot.
 */
export function loadDesktopSettings(): DesktopSettings {
	return readBlob();
}

/**
 * Merge `changes` into the persisted DesktopSettings bag. Only `undefined`
 * values are skipped — explicit `null` is honoured so callers can clear a key.
 *
 * Side effects:
 * - `homeDirectory` is mirrored to `lis.directory.home` so `useDirectoryStore`
 *   can hydrate its `homeDirectory` slot on first paint.
 * - `themeVariant` + `useSystemTheme` are mirrored to their own keys so
 *   `initializeAppearancePreferences` can act synchronously at boot.
 */
export async function updateDesktopSettings(changes: Partial<DesktopSettings>): Promise<void> {
	if (!changes || typeof changes !== 'object') return;
	const storage = safeStorage();
	if (!storage) return;

	const current = readBlob();
	const next: DesktopSettings = { ...current };
	for (const [key, value] of Object.entries(changes) as [keyof DesktopSettings, unknown][]) {
		if (value === undefined) continue;
		(next[key] as unknown) = value;
	}
	writeBlob(next);

	if (typeof next.homeDirectory === 'string' && next.homeDirectory.length > 0) {
		try { storage.setItem(HOME_KEY, next.homeDirectory); } catch { /* swallow */ }
	}
	if (next.themeVariant === 'light' || next.themeVariant === 'dark') {
		try { storage.setItem(THEME_VARIANT, next.themeVariant); } catch { /* swallow */ }
	}
	if (typeof next.useSystemTheme === 'boolean') {
		try { storage.setItem(SYSTEM_THEME, String(next.useSystemTheme)); } catch { /* swallow */ }
	}
}

/**
 * Pull settings from the (non-existent) remote settings API. In OC this is a
 * `fetch(/api/settings)` round-trip. Lis has no such endpoint — the
 * extension host is the source of truth for the things we care about, and it
 * ships them via `lis.workspace` / `lis.init` postMessages.
 */
export async function syncDesktopSettings(): Promise<void> { /* noop — Lis has no remote settings API */ }

/**
 * Invalidates the OC settings cache. Lis doesn't cache so this is a noop;
 * kept for OC parity.
 */
export function invalidateSettingsCache(): void { /* noop */ }

/**
 * Apply persisted appearance preferences before React renders, so the user
 * doesn't see a flash of the default theme. Reads the dedicated top-level
 * keys (faster than the JSON blob) and flips `<html>` classes accordingly.
 *
 * VS Code injects its own `--vscode-*` colours, but the OC components rely on
 * the `dark` class on `<html>` for the OC-derived design tokens. The default
 * (no class) is light; we add `dark` when the user chose it explicitly or
 * when `useSystemTheme` is on and the OS prefers dark.
 */
export async function initializeAppearancePreferences(): Promise<void> {
	if (typeof document === 'undefined') return;
	const storage = safeStorage();

	let variant: 'light' | 'dark' | null = null;
	try {
		const rawVariant = storage?.getItem(THEME_VARIANT);
		if (rawVariant === 'light' || rawVariant === 'dark') variant = rawVariant;
	} catch { /* swallow */ }

	let useSystem = false;
	try {
		const rawSystem = storage?.getItem(SYSTEM_THEME);
		if (rawSystem === 'true') useSystem = true;
	} catch { /* swallow */ }

	let resolved: 'light' | 'dark' = variant ?? 'dark'; // OC default → dark
	if (useSystem && typeof window !== 'undefined' && typeof window.matchMedia === 'function') {
		try {
			resolved = window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
		} catch { /* fall through to variant */ }
	}

	document.documentElement.classList.toggle('dark', resolved === 'dark');
}
