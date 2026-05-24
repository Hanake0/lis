import { create } from 'zustand';
const noop: any = () => {};
export const useAgentGroupsStore = create(() => ({ groups: [] }));
