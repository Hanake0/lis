import { afterEach, beforeEach, describe, expect, test } from 'bun:test';

import { loadPersistedDirectory, persistDirectory } from './directoryPersistence';

class FakeStorage implements Storage {
	private store = new Map<string, string>();
	get length(): number { return this.store.size; }
	clear(): void { this.store.clear(); }
	getItem(key: string): string | null { return this.store.get(key) ?? null; }
	key(index: number): string | null { return [...this.store.keys()][index] ?? null; }
	removeItem(key: string): void { this.store.delete(key); }
	setItem(key: string, value: string): void { this.store.set(key, value); }
}

describe('directoryPersistence', () => {
	const g = globalThis as unknown as { window?: { localStorage: Storage } };
	const prev = g.window;
	let storage: FakeStorage;

	beforeEach(() => {
		storage  = new FakeStorage();
		g.window = { localStorage: storage };
	});

	afterEach(() => { g.window = prev; });

	test('persistDirectory writes the trimmed path to localStorage', () => {
		persistDirectory('  /repo  ');
		expect(storage.getItem('lis.directory.last')).toBe('/repo');
	});

	test('persistDirectory(empty) clears the key', () => {
		persistDirectory('/repo');
		persistDirectory('');
		expect(storage.getItem('lis.directory.last')).toBeNull();
	});

	test('loadPersistedDirectory round-trips a saved path', () => {
		persistDirectory('/repo');
		expect(loadPersistedDirectory()).toBe('/repo');
	});

	test('loadPersistedDirectory returns null when nothing is saved', () => {
		expect(loadPersistedDirectory()).toBeNull();
	});
});
