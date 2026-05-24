import { context, build } from 'esbuild';

const watch = process.argv.includes('--watch');

const config = {
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

if (watch) {
	const ctx = await context(config);
	await ctx.watch();
	console.log('watching for changes...');
} else {
	await build(config);
}
