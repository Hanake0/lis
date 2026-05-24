import { create } from 'zustand';
const noop: any = () => {};
export const useGitHubPrStatusStore = create(() => ({ statuses: new Map() }));
export function getGitHubPrStatusKey(..._args: any[]): any { return null; }
export function usePrVisualSummaryByKeys(..._args: any[]): any { return null; }
