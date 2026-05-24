import { create } from 'zustand';

const noop: any = () => {};

export const useProjectsStore = create<any>((set: any) => ({
	activeProjectId: null as string | null,
	projects: [] as any[],
	setActiveProjectId: (id: string | null) => set({ activeProjectId: id }),
	setActiveProjectIdOnly: noop,
	setActiveProject: noop,
	getActiveProject: () => null,
	addProject: noop,
	removeProject: noop,
	removeProjectIcon: noop,
	reorderProjects: noop,
	updateProjectMeta: noop,
	uploadProjectIcon: noop,
	discoverProjectIcon: noop,
}));
