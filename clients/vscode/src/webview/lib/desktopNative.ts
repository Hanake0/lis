/**
 * OC desktop's native-shell IPC (window controls, clipboard, etc.) routed
 * through Tauri/Electron. Lis has none of that — VS Code's webview is
 * sandboxed and any "native" action goes through `host_open_external` or
 * a dedicated extension-host message. Stubs return null so OC components
 * can compile and fall through to web-API equivalents.
 */

export interface DesktopNative {
	platform?: 'darwin' | 'linux' | 'win32';
	version?:  string;
}

export function getDesktopNative(): DesktopNative | null { return null; }

/**
 * Invoke an arbitrary native command — Tauri/Electron only. Returns null in Lis;
 * callers that need a working path should fall back to web APIs or postMessage.
 */
export async function invokeDesktopCommand<T = unknown>(_command: string, _args?: Record<string, unknown>): Promise<T | null> {
	return null;
}
