import { context, build } from 'esbuild';

const watch = process.argv.includes('--watch');

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
