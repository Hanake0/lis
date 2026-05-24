import { create } from 'zustand';

export type MainTab = 'chat' | 'plan' | 'git' | 'diff' | 'terminal' | 'files' | 'context';
export type RightSidebarTab = 'git' | 'files' | 'context';
export type MermaidRenderingMode = 'svg' | 'ascii';
export type UserMessageRenderingMode = 'markdown' | 'plain';
export type ChatRenderMode = 'sorted' | 'live';
export type ActivityRenderMode = 'collapsed' | 'summary';

interface UIStore {
	activeMainTab: MainTab;
	isExpandedInput: boolean;
	stickyUserHeader: boolean;
	wideChatLayoutEnabled: boolean;
	isAboutDialogOpen: boolean;
	isCommandPaletteOpen: boolean;
	isHelpDialogOpen: boolean;
	isImagePreviewOpen: boolean;
	isMultiRunLauncherOpen: boolean;
	isSessionSwitcherOpen: boolean;
	isSettingsDialogOpen: boolean;
	isTimelineDialogOpen: boolean;
	mermaidRenderingMode: MermaidRenderingMode;
	userMessageRenderingMode: UserMessageRenderingMode;
	chatRenderMode: ChatRenderMode;
	activityRenderMode: ActivityRenderMode;
	setActiveMainTab: (tab: MainTab) => void;
	setExpandedInput: (v: boolean) => void;
	setTimelineDialogOpen: (v: boolean) => void;
	setStickyUserHeader: (v: boolean) => void;
	setWideChatLayoutEnabled: (v: boolean) => void;
	setAboutDialogOpen: (v: boolean) => void;
	setCommandPaletteOpen: (v: boolean) => void;
	setHelpDialogOpen: (v: boolean) => void;
	setImagePreviewOpen: (v: boolean) => void;
	setSessionSwitcherOpen: (v: boolean) => void;
	setSettingsDialogOpen: (v: boolean) => void;
}

export const useUIStore = create<UIStore>((set) => ({
	activeMainTab: 'chat',
	isExpandedInput: false,
	stickyUserHeader: false,
	wideChatLayoutEnabled: false,
	isAboutDialogOpen: false,
	isCommandPaletteOpen: false,
	isHelpDialogOpen: false,
	isImagePreviewOpen: false,
	isMultiRunLauncherOpen: false,
	isSessionSwitcherOpen: false,
	isSettingsDialogOpen: false,
	isTimelineDialogOpen: false,
	mermaidRenderingMode: 'svg',
	userMessageRenderingMode: 'markdown',
	chatRenderMode: 'sorted',
	activityRenderMode: 'collapsed',
	setActiveMainTab: (tab) => set({ activeMainTab: tab }),
	setExpandedInput: (v) => set({ isExpandedInput: v }),
	setTimelineDialogOpen: (v) => set({ isTimelineDialogOpen: v }),
	setStickyUserHeader: (v) => set({ stickyUserHeader: v }),
	setWideChatLayoutEnabled: (v) => set({ wideChatLayoutEnabled: v }),
	setAboutDialogOpen: (v) => set({ isAboutDialogOpen: v }),
	setCommandPaletteOpen: (v) => set({ isCommandPaletteOpen: v }),
	setHelpDialogOpen: (v) => set({ isHelpDialogOpen: v }),
	setImagePreviewOpen: (v) => set({ isImagePreviewOpen: v }),
	setSessionSwitcherOpen: (v) => set({ isSessionSwitcherOpen: v }),
	setSettingsDialogOpen: (v) => set({ isSettingsDialogOpen: v }),
}));
