import { create } from 'zustand';
const noop: any = () => {};
export const useAgentGroupsStore = create<any>(() => ({
	groups: [], groupsByAgent: {}, agentsByGroup: {},
	selectedSessionId: null, selectedGroupName: null, isLoading: false,
	getGroupForAgent: () => null, getAgentsInGroup: () => [],
	setGroups: noop, createGroup: noop, deleteGroup: noop,
	selectSession: noop, selectGroup: noop, loadGroups: noop, deleteGroupSessions: noop,
}));