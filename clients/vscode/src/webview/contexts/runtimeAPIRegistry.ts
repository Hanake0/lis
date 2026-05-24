import type { RuntimeAPIs } from '@/lib/api/types';

let registered: RuntimeAPIs | null = null;

export function registerRuntimeAPIs(apis: RuntimeAPIs | null): void {
	registered = apis;
}

export function getRegisteredRuntimeAPIs(): RuntimeAPIs | null {
	return registered;
}
