import { describe, expect, test } from 'bun:test';

import {
	canUseElectronDesktopIPC,
	getDesktopHomeDirectory,
	getDesktopLanAddress,
	invokeDesktop,
	isDesktopLocalOriginActive,
	isDesktopShell,
	isElectronRuntime,
	isElectronShell,
	isTauriShell,
	isVSCodeRuntime,
	isWebRuntime,
	openDesktopFileInApp,
	openDesktopPath,
	requestDirectoryAccess,
	requestFileAccess,
	restartDesktopApp,
	sendAssistantCompletionNotification,
	startDesktopWindowDrag,
} from './desktop';

describe('desktop runtime detection — Lis is VS Code only', () => {
	test('isVSCodeRuntime is true', () => {
		expect(isVSCodeRuntime()).toBe(true);
	});

	test('all other shells return false', () => {
		expect(isTauriShell()).toBe(false);
		expect(isElectronShell()).toBe(false);
		expect(isElectronRuntime()).toBe(false);
		expect(isDesktopShell()).toBe(false);
		expect(isWebRuntime()).toBe(false);
		expect(isDesktopLocalOriginActive()).toBe(false);
		expect(canUseElectronDesktopIPC()).toBe(false);
	});
});

describe('desktop IPC shims', () => {
	test('invokeDesktop returns null', async () => {
		expect(await invokeDesktop('whatever')).toBeNull();
	});

	test('getDesktopLanAddress returns null', () => {
		expect(getDesktopLanAddress()).toBeNull();
	});

	test('getDesktopHomeDirectory returns null (host injects via lis.workspace)', async () => {
		expect(await getDesktopHomeDirectory()).toBeNull();
	});

	test('restartDesktopApp returns false', async () => {
		expect(await restartDesktopApp()).toBe(false);
	});

	test('startDesktopWindowDrag returns false', async () => {
		expect(await startDesktopWindowDrag()).toBe(false);
	});

	test('sendAssistantCompletionNotification returns false', async () => {
		expect(await sendAssistantCompletionNotification({ title: 't', body: 'b' })).toBe(false);
	});
});

describe('native file/folder pickers', () => {
	test('requestDirectoryAccess succeeds with the passed-in path (matches OC web fallback)', async () => {
		const r = await requestDirectoryAccess('/some/path');
		expect(r).toEqual({ success: true, path: '/some/path' });
	});

	test('requestFileAccess refuses with a clear error', async () => {
		const r = await requestFileAccess();
		expect(r.success).toBe(false);
		expect(r.error).toContain('Native file picker not available');
	});
});

describe('open-path — dispatches host_open_external when VS Code API is present', () => {
	test('openDesktopPath returns false on blank input', async () => {
		expect(await openDesktopPath('')).toBe(false);
		expect(await openDesktopPath('   ')).toBe(false);
	});

	test('openDesktopPath returns false when no VS Code API is available (test runtime)', async () => {
		// In bun's test runtime there's no `window.acquireVsCodeApi`, so the post
		// can't reach the host — the function should report false rather than throw.
		expect(await openDesktopPath('/tmp/file')).toBe(false);
	});

	test('openDesktopFileInApp validates both args', async () => {
		expect(await openDesktopFileInApp('', 'app', 'App Name')).toBe(false);
		expect(await openDesktopFileInApp('/tmp/file', '', 'App Name')).toBe(false);
	});

	test('openDesktopPath dispatches the host_open_external envelope when API is wired', async () => {
		const posted: unknown[] = [];
		const fakeApi = { postMessage: (m: unknown) => { posted.push(m); } };
		const g = globalThis as unknown as { window?: { acquireVsCodeApi?: () => typeof fakeApi } };
		const prev = g.window;
		g.window = { acquireVsCodeApi: () => fakeApi };
		try {
			const ok = await openDesktopPath('/tmp/foo', 'com.apple.TextEdit');
			expect(ok).toBe(true);
			expect(posted).toEqual([{ type: 'host_open_external', path: '/tmp/foo', app: 'com.apple.TextEdit' }]);
		} finally {
			if (prev !== undefined) g.window = prev; else delete g.window;
		}
	});
});
