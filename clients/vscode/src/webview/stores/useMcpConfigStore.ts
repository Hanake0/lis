import { create } from 'zustand';
const noop: any = () => {};
const asyncNoop: any = async () => {};
export const useMcpConfigStore = create<any>(() => ({
	configs: [], servers: [],
}));
export function envRecordToArray(..._a: any[]): any { return []; }