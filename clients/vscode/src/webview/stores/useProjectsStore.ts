import { create } from 'zustand';

interface ProjectsStore {
	activeProjectId: string | null;
	setActiveProjectId: (id: string | null) => void;
}

export const useProjectsStore = create<ProjectsStore>((set) => ({
	activeProjectId: null,
	setActiveProjectId: (id) => set({ activeProjectId: id }),
}));
