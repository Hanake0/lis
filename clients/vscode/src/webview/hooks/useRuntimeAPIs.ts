import type { RuntimeAPIs } from '@/lib/api/types';

const EMPTY_APIS: RuntimeAPIs = {};

export function useRuntimeAPIs(): RuntimeAPIs { return EMPTY_APIS; }
export function useIsVSCodeRuntime(): boolean { return true; }
