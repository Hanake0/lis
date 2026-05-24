import { create } from 'zustand';
const noop: any = () => {};
const EMPTY_MAP: Map<string, any> = new Map();
export const useGitHubPrStatusStore = create<any>(() => ({
	statuses: EMPTY_MAP, prsBySession: EMPTY_MAP, isLoading: false,
	fetchStatus: noop, clearStatus: noop, getPrStatus: () => null, hasPrForSession: () => false,
	ensureEntry: noop, setParams: noop, refreshTargets: noop,
	startWatching: noop, stopWatching: noop, refresh: noop, updateStatus: noop,
}));
export function getGitHubPrStatusKey(): string { return ''; }
export function usePrVisualSummaryByKeys(): any { return EMPTY_MAP; }