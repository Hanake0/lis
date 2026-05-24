import { create } from 'zustand';
const noop: any = () => {};
export const useOpenInAppsStore = create(() => ({ apps: [] }));
