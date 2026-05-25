import { context, build } from 'esbuild';
import path from 'path';
import { fileURLToPath } from 'url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const watch = process.argv.includes('--watch');

const sdkStub = path.resolve(__dirname, 'src/webview/lib/opencode-sdk-stub.ts');
const tauriStub = path.resolve(__dirname, 'src/webview/lib/tauri-stub.ts');
const pierreStub = path.resolve(__dirname, 'src/webview/lib/pierre-stub.ts');

/** @type {import('esbuild').Plugin} */
const aliasPlugin = {
	name: 'alias',
	setup(b) {
		b.onResolve({ filter: /^@\// }, async (args) => {
			const resolved = await b.resolve(
				'./' + path.relative(
					path.dirname(args.importer || path.join(__dirname, 'src/webview/index.ts')),
					path.join(__dirname, 'src/webview', args.path.slice(2)),
				).replace(/\\/g, '/'),
				{ importer: args.importer, resolveDir: path.dirname(args.importer || path.join(__dirname, 'src/webview/index.ts')), kind: args.kind },
			);
			return resolved;
		});
		b.onResolve({ filter: /^@opencode-ai\/sdk/ }, () => ({ path: sdkStub }));
		b.onResolve({ filter: /^@tauri-apps\// }, () => ({ path: tauriStub }));
		b.onResolve({ filter: /^@pierre\// }, () => ({ path: pierreStub }));
	},
};

const extensionConfig = {
	entryPoints: ['src/extension.ts'],
	bundle: true,
	outfile: 'dist/extension.js',
	// `fs` is a Node built-in that the VS Code extension host resolves at
	// runtime — marking it external keeps esbuild from trying to bundle it.
	external: ['vscode', 'fs'],
	format: 'cjs',
	platform: 'browser',
	target: 'es2022',
	sourcemap: true,
	minify: !watch,
	logLevel: 'info',
};

const webviewConfig = {
	entryPoints: ['src/webview/index.ts'],
	bundle: true,
	outfile: 'dist/chat.js',
	format: 'iife',
	platform: 'browser',
	target: 'es2022',
	sourcemap: true,
	minify: !watch,
	logLevel: 'info',
	jsx: 'automatic',
	plugins: [aliasPlugin],
	loader: {
		'.ttf': 'dataurl',
		'.woff': 'dataurl',
		'.woff2': 'dataurl',
	},
};

// Standalone bundle for the Add-Project picker webview. Same React/Tailwind/
// alias setup as chat.js but a smaller entry that only pulls in
// DirectoryExplorerDialog + its deps — keeps the picker tab snappy.
const addProjectConfig = {
	...webviewConfig,
	entryPoints: ['src/webview/addProject.ts'],
	outfile: 'dist/addProject.js',
};

if (watch) {
	const [extCtx, wvCtx, apCtx] = await Promise.all([
		context(extensionConfig),
		context(webviewConfig),
		context(addProjectConfig),
	]);
	await Promise.all([extCtx.watch(), wvCtx.watch(), apCtx.watch()]);
	console.log('watching for changes...');
} else {
	await Promise.all([build(extensionConfig), build(webviewConfig), build(addProjectConfig)]);
}
