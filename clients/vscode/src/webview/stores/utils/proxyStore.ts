import { create, type StateCreator } from 'zustand';

const noop: any = () => {};

const EMPTY_ARR: any[] = [];
const defaultCache = new Map<string, any>();

const safeDefault = (key: string): any => {
	const cached = defaultCache.get(key);
	if (cached !== undefined) return cached;

	let val: any;
	if (key.startsWith('set') || key.startsWith('toggle') || key.startsWith('add') ||
		key.startsWith('remove') || key.startsWith('clear') || key.startsWith('reset') ||
		key.startsWith('update') || key.startsWith('load') || key.startsWith('fetch') ||
		key.startsWith('delete') || key.startsWith('create') || key.startsWith('reorder') ||
		key.startsWith('discover') || key.startsWith('upload') || key.startsWith('navigate') ||
		key.startsWith('open') || key.startsWith('close') || key.startsWith('refresh') ||
		key.startsWith('init') || key.startsWith('save') || key.startsWith('install') ||
		key.startsWith('connect') || key.startsWith('disconnect') || key.startsWith('start') ||
		key.startsWith('stop') || key.startsWith('retry') || key.startsWith('abort') ||
		key.startsWith('send') || key.startsWith('cancel') || key.startsWith('move') ||
		key.startsWith('rename') || key.startsWith('import') || key.startsWith('export') ||
		key.startsWith('apply') || key.startsWith('respond') || key.startsWith('reject') ||
		key.startsWith('acknowledge') || key.startsWith('scan') || key.startsWith('cleanup') ||
		key.startsWith('consume') || key.startsWith('append') || key.startsWith('restore') ||
		key.startsWith('bump') || key.startsWith('prefetch') || key.startsWith('ensure') ||
		key.startsWith('select') || key.startsWith('prune') || key.startsWith('upsert') ||
		key.startsWith('complete') || key.startsWith('expand') || key.startsWith('collapse') ||
		key.startsWith('handle') || key.startsWith('fork') || key.startsWith('revert') ||
		key.startsWith('share') || key.startsWith('unshare') || key.startsWith('archive') ||
		key.startsWith('schedule')) {
		val = noop;
	} else if (key.startsWith('get') || key.startsWith('find') || key.startsWith('resolve')) {
		val = () => null;
	} else if (key.startsWith('is') || key.startsWith('has') || key.startsWith('show') || key.startsWith('enabled')) {
		val = false;
	} else {
		val = EMPTY_ARR;
	}
	defaultCache.set(key, val);
	return val;
};

const ZUSTAND_INTERNALS = new Set([
	'setState', 'getState', 'subscribe', 'destroy',
	'$$typeof', 'getInitialState',
]);

export function createProxyStore<T extends Record<string, any>>(
	initializer: StateCreator<T, [], []>
) {
	const raw = create<T>(initializer);

	const originalGetState = raw.getState.bind(raw);

	const proxyHandler: ProxyHandler<T> = {
		get(target, prop: string) {
			if (prop in target) return (target as any)[prop];
			return safeDefault(prop);
		},
	};

	const patchedGetState = () => new Proxy(originalGetState(), proxyHandler);

	const patched: typeof raw = (selector?: any, equalityFn?: any) => {
		if (typeof selector === 'function') {
			const proxyState = new Proxy(originalGetState(), proxyHandler);
			const selected = selector(proxyState);
			return raw(() => selected, equalityFn);
		}
		return raw(selector, equalityFn);
	};

	patched.getState = patchedGetState;
	patched.setState = raw.setState.bind(raw);
	patched.subscribe = raw.subscribe.bind(raw);
	(patched as any).destroy = (raw as any).destroy?.bind(raw);

	return patched;
}
