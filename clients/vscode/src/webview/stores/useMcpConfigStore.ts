import { create } from 'zustand';
const noop: any = () => {};
const EMPTY_ARR: any[] = [];
export const useMcpConfigStore = create<any>(() => ({
	configs: EMPTY_ARR, servers: EMPTY_ARR, mcpServers: EMPTY_ARR, loadMcpConfigs: noop,
}));
export function envRecordToArray(): any[] { return EMPTY_ARR; }