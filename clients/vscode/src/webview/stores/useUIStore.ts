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

const noop: any = () => {};

export const useUIStore = create<any>((set: any) => ({
	activeMainTab: 'chat' as MainTab,
	isExpandedInput: false,
	stickyUserHeader: false,
	wideChatLayoutEnabled: false,
	isMobile: false,

	isSidebarOpen: false,
	sidebarWidth: 0,
	isRightSidebarOpen: false,
	rightSidebarWidth: 360,
	rightSidebarTab: 'git' as RightSidebarTab,
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
	isModelSelectorOpen: false,
	isOpenCodeStatusDialogOpen: false,
	isScheduledTasksDialogOpen: false,
	isSessionDropdownOpen: false,
	isMobileSessionStatusBarCollapsed: false,
	mermaidRenderingMode: 'svg',
	userMessageRenderingMode: 'markdown',
	chatRenderMode: 'standard',
	activityRenderMode: 'collapsed',
	diffViewMode: 'unified',
	diffFileLayout: 'list',
	diffWrapLines: false,
	diffLayoutPreference: 'auto',
	gitChangesViewMode: 'tree',
	timeFormatPreference: 'auto',
	weekStartPreference: 'auto',
	sessionRetentionAction: 'archive',
	autoDeleteEnabled: false,
	autoDeleteAfterDays: 30,
	collapsibleThinkingBlocks: true,
	groupReasoningBlocks: false,
	inputSpellcheckEnabled: false,
	showSplitAssistantMessageActions: false,
	showToolFileIcons: true,
	showTerminalQuickKeysOnDesktop: false,
	showMobileSessionStatusBar: true,
	showOpenCodeUpdateNotifications: true,
	notifyOnSubtasks: false,
	showDeletionDialog: false,
	persistChatDraft: false,
	padding: 1,
	inputBarOffset: 0,
	notesPanelHeight: 200,
	todoPanelHeight: 200,
	multiRunLauncherPrefillPrompt: null as string | null,
	pendingDiffFile: null,
	pendingDiffStaged: false,
	pendingFileFocusPath: null,
	pendingFileNavigation: null,
	settingsPage: null,
	settingsProjectsSelectedId: null,
	settingsRemoteInstancesSelectedId: null,
	openCodeStatusText: '',
	hiddenModels: [] as string[],
	shortcutOverrides: {} as Record<string, any>,
	contextPanelByDirectory: {} as Record<string, any>,
	hasManuallyResizedLeftSidebar: false,
	hasManuallyResizedRightSidebar: false,

	setActiveMainTab: (tab: MainTab) => set({ activeMainTab: tab }),
	setExpandedInput: (v: boolean) => set({ isExpandedInput: v }),
	setTimelineDialogOpen: (v: boolean) => set({ isTimelineDialogOpen: v }),
	setStickyUserHeader: (v: boolean) => set({ stickyUserHeader: v }),
	setWideChatLayoutEnabled: (v: boolean) => set({ wideChatLayoutEnabled: v }),
	setIsMobile: (v: boolean) => set({ isMobile: v }),

	toggleSidebar: () => {
		try { (window as any).acquireVsCodeApi?.()?.postMessage({ type: 'toggle_sidebar' }); } catch {}
	},
	setSidebarOpen: (_open: boolean) => {},
	setSidebarWidth: (_width: number) => {},
	toggleRightSidebar: () => set((s: any) => ({ isRightSidebarOpen: !s.isRightSidebarOpen })),
	setRightSidebarOpen: (open: boolean) => set({ isRightSidebarOpen: open }),
	setRightSidebarWidth: (width: number) => set({ rightSidebarWidth: width }),
	setRightSidebarTab: (tab: RightSidebarTab) => set({ rightSidebarTab: tab }),
	setBottomTerminalOpen: (open: boolean) => set({ isBottomTerminalOpen: open }),
	setBottomTerminalExpanded: (expanded: boolean) => set({ isBottomTerminalExpanded: expanded }),
	setBottomTerminalHeight: (height: number) => set({ bottomTerminalHeight: height }),
	toggleBottomTerminal: () => set((s: any) => ({ isBottomTerminalOpen: !s.isBottomTerminalOpen })),

	setAboutDialogOpen: (v: boolean) => set({ isAboutDialogOpen: v }),
	setCommandPaletteOpen: (v: boolean) => set({ isCommandPaletteOpen: v }),
	setHelpDialogOpen: (v: boolean) => set({ isHelpDialogOpen: v }),
	setImagePreviewOpen: (v: boolean) => set({ isImagePreviewOpen: v }),
	setSessionSwitcherOpen: (v: boolean) => set({ isSessionSwitcherOpen: v }),
	setSettingsDialogOpen: (v: boolean) => set({ isSettingsDialogOpen: v }),
	setMultiRunLauncherOpen: (v: boolean) => set({ isMultiRunLauncherOpen: v }),
	setModelSelectorOpen: (v: boolean) => set({ isModelSelectorOpen: v }),
	setOpenCodeStatusDialogOpen: (v: boolean) => set({ isOpenCodeStatusDialogOpen: v }),
	setScheduledTasksDialogOpen: (v: boolean) => set({ isScheduledTasksDialogOpen: v }),
	setSessionDropdownOpen: (v: boolean) => set({ isSessionDropdownOpen: v }),
	setIsMobileSessionStatusBarCollapsed: (v: boolean) => set({ isMobileSessionStatusBarCollapsed: v }),
	setShowDeletionDialog: (v: boolean) => set({ showDeletionDialog: v }),
	setShowOpenCodeUpdateNotifications: (v: boolean) => set({ showOpenCodeUpdateNotifications: v }),

	// Panel-height tracking actually consumed by ProjectNotesTodoPanel — must persist
	// the height across re-renders or the drag handle resets to the default on every mount.
	setNotesPanelHeight: (h: number) => set({ notesPanelHeight: h }),
	setTodoPanelHeight:  (h: number) => set({ todoPanelHeight:  h }),

	// Settings navigation: GitHubIntegrationDialog + GitHubIssuePickerDialog + GitHubPrPickerDialog
	// all open the settings dialog onto a specific page (`github`). Without this, the dialog
	// opens but the panel doesn't focus.
	setSettingsPage: (page: string | null) => set({ settingsPage: page }),
	setSettingsProjectsSelectedId:        (id: string | null) => set({ settingsProjectsSelectedId:        id }),
	setSettingsRemoteInstancesSelectedId: (id: string | null) => set({ settingsRemoteInstancesSelectedId: id }),

	// Help dialog toggle — bound to the sidebar's `?` button.
	toggleHelpDialog: () => set((s: { isHelpDialogOpen: boolean }) => ({ isHelpDialogOpen: !s.isHelpDialogOpen })),

	// Multi-run launcher entry points — the sidebar dispatches these when the user clicks the launcher button.
	openMultiRunLauncher: () => set({ isMultiRunLauncherOpen: true, multiRunLauncherPrefillPrompt: null }),
	openMultiRunLauncherWithPrompt: (prompt: string | null) => set({
		isMultiRunLauncherOpen:         true,
		multiRunLauncherPrefillPrompt:  prompt,
	}),

	// Favorite-model predicate. Returns false until the favorites feature ships;
	// keeps the type as a boolean-returning function so consumers don't crash on `.call()`.
	isFavoriteModel: () => false,

	// ── Below this line: features that don't exist in the Lis sidebar today. ──
	// Diff/git-change view modes (chat-panel features), context panel (OC desktop),
	// remote shortcuts, recent-model tracking, etc. Kept as noops so any byte-identical
	// OC component that imports them still compiles; consumers see graceful "no change".
	setDiffViewMode: noop,
	setDiffFileLayout: noop,
	setDiffWrapLines: noop,
	setGitChangesViewMode: noop,
	setSessionRetentionAction: noop,
	setAutoDeleteEnabled: noop,
	setAutoDeleteAfterDays: noop,
	setPendingDiffFile: noop,
	setPendingFileFocusPath: noop,
	setPendingFileNavigation: noop,
	setMainTabGuard: noop,
	setShortcutOverride: noop,
	clearShortcutOverride: noop,
	resetAllShortcutOverrides: noop,
	setActiveContextPanelTab: noop,
	setContextPanelTabTargetPath: noop,
	setContextPanelWidth: noop,

	toggleSidebar2: noop,
	toggleFavoriteModel: noop,
	toggleHiddenModel: noop,
	toggleContextPanelExpanded: noop,

	openContextFile: noop,
	openContextFileAtLine: noop,
	openContextDiff: noop,
	openContextPlan: noop,
	openContextPreview: noop,
	openContextBrowser: noop,
	openContextOverview: noop,
	openContextPanelTab: noop,
	closeContextPanel: noop,
	closeContextPanelTab: noop,
	reorderContextPanelTabs: noop,
	navigateToDiff: noop,
	addRecentModel: noop,
	addRecentAgent: noop,
	addRecentEffort: noop,
	reorderFavoriteModel: noop,
	showAllModels: noop,
	hideAllModels: noop,
	updateProportionalSidebarWidths: noop,
}));
