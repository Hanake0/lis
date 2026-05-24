import { create } from 'zustand';

export type MainTab = 'chat' | 'plan' | 'git' | 'diff' | 'terminal' | 'files' | 'context';
export type RightSidebarTab = 'git' | 'files' | 'context';
export type ContextPanelMode = 'diff' | 'file' | 'context' | 'plan' | 'chat' | 'preview' | 'browser';
export type MermaidRenderingMode = 'svg' | 'ascii';
export type UserMessageRenderingMode = 'markdown' | 'plain';
export type ChatRenderMode = 'sorted' | 'live';
export type ActivityRenderMode = 'collapsed' | 'summary';
export type SessionRetentionAction = 'archive' | 'delete';
export type TimeFormatPreference = 'auto' | '12h' | '24h';
export type WeekStartPreference = 'auto' | 'sunday' | 'monday';
export type MainTabGuard = (nextTab: MainTab) => boolean;
export type EventStreamStatus = 'idle' | 'connecting' | 'connected' | 'reconnecting' | 'paused' | 'offline' | 'error';

interface UIStore {
	activeMainTab: MainTab;
	isExpandedInput: boolean;
	stickyUserHeader: boolean;
	wideChatLayoutEnabled: boolean;

	isSidebarOpen: boolean;
	sidebarWidth: number;
	isRightSidebarOpen: boolean;
	rightSidebarWidth: number;
	rightSidebarTab: RightSidebarTab;
	isBottomTerminalOpen: boolean;
	isBottomTerminalExpanded: boolean;
	bottomTerminalHeight: number;

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

	hasManuallyResizedLeftSidebar: boolean;
	hasManuallyResizedRightSidebar: boolean;

	setActiveMainTab: (tab: MainTab) => void;
	setExpandedInput: (v: boolean) => void;
	setTimelineDialogOpen: (v: boolean) => void;
	setStickyUserHeader: (v: boolean) => void;
	setWideChatLayoutEnabled: (v: boolean) => void;

	toggleSidebar: () => void;
	setSidebarOpen: (open: boolean) => void;
	setSidebarWidth: (width: number) => void;
	toggleRightSidebar: () => void;
	setRightSidebarOpen: (open: boolean) => void;
	setRightSidebarWidth: (width: number) => void;
	setRightSidebarTab: (tab: RightSidebarTab) => void;
	setBottomTerminalOpen: (open: boolean) => void;
	setBottomTerminalExpanded: (expanded: boolean) => void;
	setBottomTerminalHeight: (height: number) => void;

	setAboutDialogOpen: (v: boolean) => void;
	setCommandPaletteOpen: (v: boolean) => void;
	setHelpDialogOpen: (v: boolean) => void;
	setImagePreviewOpen: (v: boolean) => void;
	setSessionSwitcherOpen: (v: boolean) => void;
	setSettingsDialogOpen: (v: boolean) => void;
	setMultiRunLauncherOpen: (v: boolean) => void;
}

export const useUIStore = create<UIStore>((set) => ({
	activeMainTab: 'chat',
	isExpandedInput: false,
	stickyUserHeader: false,
	wideChatLayoutEnabled: false,

	isSidebarOpen: true,
	sidebarWidth: 280,
	isRightSidebarOpen: false,
	rightSidebarWidth: 360,
	rightSidebarTab: 'git',
	isBottomTerminalOpen: false,
	isBottomTerminalExpanded: false,
	bottomTerminalHeight: 300,

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

	hasManuallyResizedLeftSidebar: false,
	hasManuallyResizedRightSidebar: false,

	setActiveMainTab: (tab) => set({ activeMainTab: tab }),
	setExpandedInput: (v) => set({ isExpandedInput: v }),
	setTimelineDialogOpen: (v) => set({ isTimelineDialogOpen: v }),
	setStickyUserHeader: (v) => set({ stickyUserHeader: v }),
	setWideChatLayoutEnabled: (v) => set({ wideChatLayoutEnabled: v }),

	toggleSidebar: () => set((s) => ({ isSidebarOpen: !s.isSidebarOpen })),
	setSidebarOpen: (open) => set({ isSidebarOpen: open }),
	setSidebarWidth: (width) => set({ sidebarWidth: width, hasManuallyResizedLeftSidebar: true }),
	toggleRightSidebar: () => set((s) => ({ isRightSidebarOpen: !s.isRightSidebarOpen })),
	setRightSidebarOpen: (open) => set({ isRightSidebarOpen: open }),
	setRightSidebarWidth: (width) => set({ rightSidebarWidth: width, hasManuallyResizedRightSidebar: true }),
	setRightSidebarTab: (tab) => set({ rightSidebarTab: tab }),
	setBottomTerminalOpen: (open) => set({ isBottomTerminalOpen: open }),
	setBottomTerminalExpanded: (expanded) => set({ isBottomTerminalExpanded: expanded }),
	setBottomTerminalHeight: (height) => set({ bottomTerminalHeight: height }),

	setAboutDialogOpen: (v) => set({ isAboutDialogOpen: v }),
	setCommandPaletteOpen: (v) => set({ isCommandPaletteOpen: v }),
	setHelpDialogOpen: (v) => set({ isHelpDialogOpen: v }),
	setImagePreviewOpen: (v) => set({ isImagePreviewOpen: v }),
	setSessionSwitcherOpen: (v) => set({ isSessionSwitcherOpen: v }),
	setSettingsDialogOpen: (v) => set({ isSettingsDialogOpen: v }),
	setMultiRunLauncherOpen: (v) => set({ isMultiRunLauncherOpen: v }),
}));
