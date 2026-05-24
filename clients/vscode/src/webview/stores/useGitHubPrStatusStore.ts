import { create } from 'zustand';
const noop: any = () => {};
export const useGitHubPrStatusStore = create<any>(() => ({
	statuses: new Map(), prsBySession: new Map(), isLoading: false,
	fetchStatus: noop, clearStatus: noop, getPrStatus: () => null, hasPrForSession: () => false,
	ensureEntry: noop, setParams: noop, refreshTargets: noop,
	startWatching: noop, stopWatching: noop, refresh: noop, updateStatus: noop,
}));
export function getGitHubPrStatusKey(): string { return ''; }
export function usePrVisualSummaryByKeys(): any { return new Map(); }