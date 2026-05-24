import { create } from 'zustand';
const noop: any = () => {};
export const useMcpConfigStore = create<any>(() => ({
	configs: [], servers: [], mcpServers: [], loadMcpConfigs: noop,
}));
export function envRecordToArray(): any[] { return []; }