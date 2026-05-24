import { create } from 'zustand';
const noop: any = () => {};
export const useMcpStore = create(() => ({ servers: [], tools: [] }));
export function computeMcpHealth(..._args: any[]): any { return null; }
