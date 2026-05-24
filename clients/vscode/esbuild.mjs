import { context, build } from 'esbuild';
import path from 'path';
import { fileURLToPath } from 'url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const watch = process.argv.includes('--watch');

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
	},
};

const extensionConfig = {
	entryPoints: ['src/extension.ts'],
	bundle: true,
	outfile: 'dist/extension.js',
	external: ['vscode'],
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
};

if (watch) {
	const [extCtx, wvCtx] = await Promise.all([
		context(extensionConfig),
		context(webviewConfig),
	]);
	await Promise.all([extCtx.watch(), wvCtx.watch()]);
	console.log('watching for changes...');
} else {
	await Promise.all([build(extensionConfig), build(webviewConfig)]);
}
