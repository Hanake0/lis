import { create } from 'zustand';
const noop: any = () => {};
const asyncNoop: any = async () => {};
export const useGitHubPrStatusStore = create<any>(() => ({
	statuses: new Map(), prsBySession: new Map(), isLoading: false,
	fetchStatus: noop, clearStatus: noop, getPrStatus: () => null, hasPrForSession: () => false,
}));
export function getGitHubPrStatusKey(..._a: any[]): any { return ''; }
export function usePrVisualSummaryByKeys(..._a: any[]): any { return {}; }