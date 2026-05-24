import { afterEach, beforeEach, describe, expect, test } from 'bun:test';

import {
	initializeAppearancePreferences,
	invalidateSettingsCache,
	loadDesktopSettings,
	syncDesktopSettings,
	updateDesktopSettings,
} from './persistence';

/** Minimal in-memory localStorage shim so tests can run under bun. */
class FakeStorage implements Storage {
	private store = new Map<string, string>();
	get length(): number { return this.store.size; }
	clear(): void { this.store.clear(); }
	getItem(key: string): string | null { return this.store.get(key) ?? null; }
	key(index: number): string | null { return [...this.store.keys()][index] ?? null; }
	removeItem(key: string): void { this.store.delete(key); }
	setItem(key: string, value: string): void { this.store.set(key, value); }
}

class FakeMediaQuery {
	constructor(public readonly matches: boolean) { /* noop */ }
	addEventListener(): void { /* noop */ }
	removeEventListener(): void { /* noop */ }
}

interface GlobalWithWindow {
	window?:   { localStorage: Storage; matchMedia?: (q: string) => FakeMediaQuery };
	document?: { documentElement: { classList: { toggle: (cls: string, on: boolean) => void } } };
}

describe('persistence', () => {
	const g = globalThis as unknown as GlobalWithWindow;
	const prev = { window: g.window, document: g.document };
	let storage: FakeStorage;
	let classList: { added: Set<string> };

	beforeEach(() => {
		storage   = new FakeStorage();
		classList = { added: new Set() };
		g.window  = { localStorage: storage, matchMedia: () => new FakeMediaQuery(false) };
		g.document = {
			documentElement: {
				classList: {
					toggle: (cls, on) => {
						if (on) classList.added.add(cls);
						else    classList.added.delete(cls);
					},
				},
			},
		};
	});

	afterEach(() => {
		g.window   = prev.window;
		g.document = prev.document;
	});

	test('updateDesktopSettings merges into the JSON blob', async () => {
		await updateDesktopSettings({ defaultModel: 'anthropic/opus' });
		await updateDesktopSettings({ defaultAgent: 'investigator' });
		const loaded = loadDesktopSettings();
		expect(loaded.defaultModel).toBe('anthropic/opus');
		expect(loaded.defaultAgent).toBe('investigator');
	});

	test('undefined values are skipped, but null is honoured (clear)', async () => {
		await updateDesktopSettings({ defaultModel: 'm1' });
		await updateDesktopSettings({ defaultModel: undefined });
		expect(loadDesktopSettings().defaultModel).toBe('m1');

		// Explicit null clears.
		await updateDesktopSettings({ defaultModel: null as never });
		expect(loadDesktopSettings().defaultModel).toBeNull();
	});

	test('homeDirectory mirrors to lis.directory.home', async () => {
		await updateDesktopSettings({ homeDirectory: '/Users/octo' });
		expect(storage.getItem('lis.directory.home')).toBe('/Users/octo');
	});

	test('themeVariant + useSystemTheme mirror to top-level keys', async () => {
		await updateDesktopSettings({ themeVariant: 'dark', useSystemTheme: true });
		expect(storage.getItem('lis.theme.variant')).toBe('dark');
		expect(storage.getItem('lis.theme.useSystem')).toBe('true');
	});

	test('syncDesktopSettings + invalidateSettingsCache are documented noops', async () => {
		await expect(syncDesktopSettings()).resolves.toBeUndefined();
		invalidateSettingsCache(); // must not throw
	});

	test('initializeAppearancePreferences defaults to dark when no preference is stored', async () => {
		await initializeAppearancePreferences();
		expect(classList.added.has('dark')).toBe(true);
	});

	test('initializeAppearancePreferences honours stored light variant', async () => {
		await updateDesktopSettings({ themeVariant: 'light' });
		await initializeAppearancePreferences();
		expect(classList.added.has('dark')).toBe(false);
	});

	test('initializeAppearancePreferences with useSystemTheme follows prefers-color-scheme', async () => {
		await updateDesktopSettings({ useSystemTheme: true, themeVariant: 'light' });
		// Override matchMedia to simulate the OS preferring dark.
		g.window!.matchMedia = () => new FakeMediaQuery(true);
		await initializeAppearancePreferences();
		expect(classList.added.has('dark')).toBe(true);
	});

	test('loadDesktopSettings returns an empty object when nothing is persisted', () => {
		expect(loadDesktopSettings()).toEqual({});
	});

	test('loadDesktopSettings tolerates corrupted JSON', () => {
		storage.setItem('lis.desktopSettings', 'not json');
		expect(loadDesktopSettings()).toEqual({});
	});
});
