import { create } from 'zustand';
export const useGitHubPrStatusStore = create(() => ({ statuses: new Map() }));
