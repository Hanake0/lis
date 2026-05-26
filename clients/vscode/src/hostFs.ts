import * as vscode from 'vscode';

import type { Logger } from './logger';

/**
 * Webview → extension host filesystem bridge.
 *
 * The OC components imported into our sidebar / chat panel (`DirectoryExplorerDialog`,
 * `DirectoryTree`, `SidebarFilesTree`, `useDirectoryStatusProbe`, ...) call into
 * `opencodeClient.listLocalDirectory` / `.probeDirectory` / `.createDirectory` /
 * `.getSystemInfo` / `.getFilesystemHome`. In Lis the webview is sandboxed and
 * cannot touch the filesystem directly — but the extension host runs in Node
 * inside VS Code's extension process, so we proxy these calls through it.
 *
 * Wire-format: webview posts `{ type: 'host_fs', op, request_id, args... }`,
 * host replies `{ type: 'host_fs_response', request_id, ok, result | error }`.
 * `request_id` is a string the webview correlates against its pending Promise map.
 */

interface HostFsRequest {
	type:        'host_fs';
	request_id:  string;
	op:          'list' | 'probe' | 'create_directory' | 'system_info' | 'home' | 'pick_files';
	path?:       string;
	allowOutsideWorkspace?: boolean;
	/** pick_files: whether the dialog should accept multiple files. */
	canSelectMany?: boolean;
	/** pick_files: file extension filters keyed by group label, e.g. `{ Images: ['png','jpg'] }`. */
	filters?: Record<string, string[]>;
}

/**
 * Install the host-side handler on a webview's `onDidReceiveMessage`. Wraps the
 * caller's existing handler so this preempts the message before the caller sees
 * it (avoids accidental "unknown message type" warnings).
 *
 * Pass the existing handler as `next` — anything that isn't a `host_fs` message
 * is forwarded unchanged.
 */
export function installHostFsHandler(
	webview: vscode.Webview,
	next:    (msg: { type?: string; [k: string]: unknown }) => void,
	logger?: Logger | null,
): vscode.Disposable {
	return webview.onDidReceiveMessage((msg: { type?: string; [k: string]: unknown }) => {
		if (msg?.type !== 'host_fs') { next(msg); return; }
		const req = msg as unknown as HostFsRequest;
		const startedAt = Date.now();
		if (logger?.isVerbose()) logger?.frame('recv', { type: 'host_fs', op: req.op, request_id: req.request_id, path: req.path });
		void handle(req)
			.then((result) => {
				if (logger?.isVerbose()) logger?.frame('send', { type: 'host_fs_response', request_id: req.request_id, ok: true, op: req.op, ms: Date.now() - startedAt });
				return webview.postMessage({ type: 'host_fs_response', request_id: req.request_id, ok: true, result });
			})
			.catch((err)   => {
				const errMsg = err instanceof Error ? err.message : String(err);
				logger?.warn(`host_fs ${req.op} failed (path=${req.path ?? '∅'}): ${errMsg}`);
				return webview.postMessage({
					type: 'host_fs_response', request_id: req.request_id, ok: false,
					error: errMsg,
				});
			});
	});
}

async function handle(req: HostFsRequest): Promise<unknown> {
	switch (req.op) {
		case 'list': {
			if (!req.path) throw new Error('host_fs.list: path is required');
			const uri = vscode.Uri.file(req.path);
			const entries = await vscode.workspace.fs.readDirectory(uri);
			// Shape required by OC's DirectoryExplorerDialog / FilesView / DirectoryTree:
			// `{ name, path, isDirectory }[]` — path is the absolute joined child.
			return entries.map(([name, type]) => ({
				name,
				path: joinPath(req.path!, name),
				isDirectory: (type & vscode.FileType.Directory) !== 0,
			}));
		}

		case 'probe': {
			if (!req.path) throw new Error('host_fs.probe: path is required');
			try {
				const stat = await vscode.workspace.fs.stat(vscode.Uri.file(req.path));
				return (stat.type & vscode.FileType.Directory) !== 0;
			} catch {
				return false;
			}
		}

		case 'create_directory': {
			if (!req.path) throw new Error('host_fs.create_directory: path is required');
			// vscode.workspace.fs.createDirectory is recursive (mkdir -p) and idempotent.
			await vscode.workspace.fs.createDirectory(vscode.Uri.file(req.path));
			return { path: req.path };
		}

		case 'system_info': {
			// OC's DirectoryTree uses this to render the user's home dir and root
			// nodes. The fields it actually reads are `home` and `platform`.
			const env = (typeof process !== 'undefined' && process.env) ? process.env : {} as NodeJS.ProcessEnv;
			const home = env.HOME ?? env.USERPROFILE
				?? (env.HOMEDRIVE && env.HOMEPATH ? `${env.HOMEDRIVE}${env.HOMEPATH}` : '');
			return { home, platform: process.platform };
		}

		case 'home': {
			const env = (typeof process !== 'undefined' && process.env) ? process.env : {} as NodeJS.ProcessEnv;
			return env.HOME ?? env.USERPROFILE
				?? (env.HOMEDRIVE && env.HOMEPATH ? `${env.HOMEDRIVE}${env.HOMEPATH}` : null);
		}

		case 'pick_files': {
			// Replaces OC's `/api/vscode/pick-files` backend endpoint. The
			// composer's paperclip button calls this when running inside a
			// VS Code webview; we open the host-side native dialog and read
			// the picked files into base64 data URLs so the webview can
			// hand them straight to its attachFiles flow.
			const uris = await vscode.window.showOpenDialog({
				canSelectFiles:   true,
				canSelectFolders: false,
				canSelectMany:    req.canSelectMany ?? true,
				openLabel:        'Attach',
				filters:          req.filters,
			});
			if (!uris || uris.length === 0) return { files: [], skipped: [] };

			const MAX_BYTES = 5 * 1024 * 1024; // 5 MiB per file — matches the inline-attachment cap on the wire.
			const files:   { name: string; mimeType: string; dataUrl: string }[] = [];
			const skipped: { name: string; reason: string }[] = [];
			for (const uri of uris) {
				try {
					const stat = await vscode.workspace.fs.stat(uri);
					if (stat.size > MAX_BYTES) {
						skipped.push({ name: basename(uri.fsPath), reason: `too large (${Math.round(stat.size / 1024)} KB)` });
						continue;
					}
					const bytes  = await vscode.workspace.fs.readFile(uri);
					const base64 = Buffer.from(bytes).toString('base64');
					const mime   = guessMimeType(uri.fsPath);
					files.push({
						name:     basename(uri.fsPath),
						mimeType: mime,
						dataUrl:  `data:${mime};base64,${base64}`,
					});
				} catch (err) {
					skipped.push({ name: basename(uri.fsPath), reason: err instanceof Error ? err.message : 'read failed' });
				}
			}
			return { files, skipped };
		}

		default:
			throw new Error(`host_fs: unknown op '${(req as { op: string }).op}'`);
	}
}

function basename(p: string): string {
	const i = Math.max(p.lastIndexOf('/'), p.lastIndexOf('\\'));
	return i < 0 ? p : p.slice(i + 1);
}

function guessMimeType(p: string): string {
	const i = p.lastIndexOf('.');
	const ext = i < 0 ? '' : p.slice(i + 1).toLowerCase();
	switch (ext) {
		case 'png':                  return 'image/png';
		case 'jpg': case 'jpeg':     return 'image/jpeg';
		case 'gif':                  return 'image/gif';
		case 'webp':                 return 'image/webp';
		case 'svg':                  return 'image/svg+xml';
		case 'bmp':                  return 'image/bmp';
		case 'pdf':                  return 'application/pdf';
		case 'txt': case 'md':       return 'text/plain';
		case 'json':                 return 'application/json';
		case 'js': case 'ts':        return 'text/javascript';
		case 'html':                 return 'text/html';
		default:                     return 'application/octet-stream';
	}
}

function joinPath(base: string, name: string): string {
	const sep = base.includes('\\') && !base.includes('/') ? '\\' : '/';
	const trimmed = base.endsWith(sep) ? base.slice(0, -1) : base;
	return `${trimmed}${sep}${name}`;
}
