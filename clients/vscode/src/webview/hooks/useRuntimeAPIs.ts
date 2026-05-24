import type { RuntimeAPIs } from '@/lib/api/types';

const EMPTY_APIS: RuntimeAPIs = {
	runtime: {
		isVSCode: true,
		isDesktop: false,
		isTauri: false,
	},
	git: null,
	github: null,
	editor: null,
	fs: null,
	terminal: null,
	openExternal: null,
};

export function useRuntimeAPIs(): RuntimeAPIs { return EMPTY_APIS; }
export function useIsVSCodeRuntime(): boolean { return true; }
