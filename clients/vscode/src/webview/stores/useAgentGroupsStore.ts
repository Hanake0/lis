import { create } from 'zustand';
const noop: any = () => {};
export const useAgentGroupsStore = create<any>(() => ({
	groups: [],
	groupsByAgent: {},
	agentsByGroup: {},
	getGroupForAgent: () => null,
	getAgentsInGroup: () => [],
	setGroups: noop,
	createGroup: noop,
	deleteGroup: noop,
}));
