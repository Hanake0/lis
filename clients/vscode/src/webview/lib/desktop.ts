/**
 * Lis runtime detection + desktop-shim. OC's `desktop.ts` carries a heavy
 * Tauri/Electron payload that doesn't apply here — Lis only runs inside the
 * VS Code webview. We re-export the OC TYPE shape verbatim (so any byte-
 * identical OC component compiles) and implement runtime detection with the
 * "always VS Code" answers. Desktop-only functions return correct nulls /
 * `{ success: false }` so consumers can branch cleanly.
 *
 * `openDesktopPath` is wired through a `host_open_external` postMessage so
 * the VS Code extension host can call `vscode.env.openExternal` — that's
 * the closest Lis equivalent to OC's tauri "open path in app" flow.
 */

import type { ProjectEntry } from '@/lib/api/types';
import type { MobileKeyboardMode } from '@/lib/mobileKeyboardMode';

// ── Types (byte-identical to OC's surface) ──────────────────────────

export type AssistantNotificationPayload = {
	title?: string;
	body?:  string;
};

export type UpdateInfo = {
	available:                 boolean;
	version?:                  string;
	currentVersion:            string;
	body?:                     string;
	date?:                     string;
	nextSuggestedCheckInSec?:  number;
	packageManager?:           string;
	updateCommand?:            string;
};

export type UpdateProgress = {
	downloaded: number;
	total?:     number;
};

export type SkillCatalogConfig = {
	id:             string;
	label:          string;
	source:         string;
	subpath?:       string;
	gitIdentityId?: string;
};

export type ManagedRemoteTunnelPreset = {
	id:       string;
	name:     string;
	hostname: string;
};

export type DesktopSettings = {
	themeId?:                              string;
	useSystemTheme?:                       boolean;
	themeVariant?:                         'light' | 'dark';
	lightThemeId?:                         string;
	darkThemeId?:                          string;
	splashBgLight?:                        string;
	splashFgLight?:                        string;
	splashBgDark?:                         string;
	splashFgDark?:                         string;
	lastDirectory?:                        string;
	homeDirectory?:                        string;
	opencodeBinary?:                       string;
	desktopLanAccessEnabled?:              boolean;
	projects?:                             ProjectEntry[];
	activeProjectId?:                      string;
	approvedDirectories?:                  string[];
	securityScopedBookmarks?:              string[];
	pinnedDirectories?:                    string[];
	showReasoningTraces?:                  boolean;
	collapsibleThinkingBlocks?:            boolean;
	showDeletionDialog?:                   boolean;
	nativeNotificationsEnabled?:           boolean;
	notificationMode?:                     'always' | 'hidden-only';
	notifyOnSubtasks?:                     boolean;
	notifyOnCompletion?:                   boolean;
	notifyOnError?:                        boolean;
	notifyOnQuestion?:                     boolean;
	notificationTemplates?: {
		completion: { title: string; message: string };
		error:      { title: string; message: string };
		question:   { title: string; message: string };
		subtask:    { title: string; message: string };
	};
	summarizeLastMessage?:                 boolean;
	summaryThreshold?:                     number;
	summaryLength?:                        number;
	maxLastMessageLength?:                 number;
	usageAutoRefresh?:                     boolean;
	usageRefreshIntervalMs?:               number;
	usageDisplayMode?:                     'usage' | 'remaining';
	usageDropdownProviders?:               string[];
	usageSelectedModels?:                  Record<string, string[]>;
	usageCollapsedFamilies?:               Record<string, string[]>;
	usageExpandedFamilies?:                Record<string, string[]>;
	usageModelGroups?:                     Record<string, {
		customGroups?:     Array<{ id: string; label: string; models: string[]; order: number }>;
		modelAssignments?: Record<string, string>;
		renamedGroups?:    Record<string, string>;
	}>;
	autoDeleteEnabled?:                    boolean;
	autoDeleteAfterDays?:                  number;
	sessionRetentionAction?:               'archive' | 'delete';
	tunnelProvider?:                       string;
	tunnelMode?:                           'quick' | 'managed-remote' | 'managed-local';
	tunnelBootstrapTtlMs?:                 number | null;
	tunnelSessionTtlMs?:                   number;
	managedLocalTunnelConfigPath?:         string | null;
	managedRemoteTunnelHostname?:          string;
	managedRemoteTunnelToken?:             string | null;
	hasManagedRemoteTunnelToken?:          boolean;
	managedRemoteTunnelPresets?:           ManagedRemoteTunnelPreset[];
	managedRemoteTunnelSelectedPresetId?:  string;
	managedRemoteTunnelPresetTokens?:      Record<string, string>;
	defaultModel?:                         string;
	defaultVariant?:                       string;
	defaultAgent?:                         string;
	defaultGitIdentityId?:                 string;
	openInAppId?:                          string;
	autoCreateWorktree?:                   boolean;
	queueModeEnabled?:                     boolean;
	gitmojiEnabled?:                       boolean;
	defaultFileViewerPreview?:             boolean;
	zenModel?:                             string;
	gitProviderId?:                        string;
	gitModelId?:                           string;
	pwaAppName?:                           string;
	pwaOrientation?:                       'system' | 'portrait' | 'landscape';
	mobileKeyboardMode?:                   MobileKeyboardMode;
	inputSpellcheckEnabled?:               boolean;
	showToolFileIcons?:                    boolean;
	showExpandedBashTools?:                boolean;
	showExpandedEditTools?:                boolean;
	timeFormatPreference?:                 'auto' | '12h' | '24h';
	weekStartPreference?:                  'auto' | 'sunday' | 'monday';
	chatRenderMode?:                       'sorted' | 'live';
	messageStreamTransport?:               'auto' | 'ws' | 'sse';
	activityRenderMode?:                   'collapsed' | 'summary';
	mermaidRenderingMode?:                 'svg' | 'ascii';
	userMessageRenderingMode?:             'markdown' | 'plain';
	stickyUserHeader?:                     boolean;
	wideChatLayoutEnabled?:                boolean;
	showSplitAssistantMessageActions?:     boolean;
	fontSize?:                             number;
	terminalFontSize?:                     number;
	uiFont?:                               string;
	monoFont?:                             string;
	padding?:                              number;
	cornerRadius?:                         number;
	inputBarOffset?:                       number;
	favoriteModels?:                       Array<{ providerID: string; modelID: string }>;
	recentModels?:                         Array<{ providerID: string; modelID: string }>;
	diffLayoutPreference?:                 'dynamic' | 'inline' | 'side-by-side';
	diffViewMode?:                         'single' | 'stacked';
	gitChangesViewMode?:                   'flat' | 'tree';
	directoryShowHidden?:                  boolean;
	filesViewShowGitignored?:              boolean;
	messageLimit?:                         number;
	skillCatalogs?:                        SkillCatalogConfig[];
	reportUsage?:                          boolean;
	globalBehaviorPrompt?:                 string;
	responseStyleEnabled?:                 boolean;
	responseStylePreset?:                  'concise' | 'detailed' | 'mentor' | 'pushback' | 'noFiller' | 'matchEnergy' | 'warmPeer' | 'custom';
	responseStyleCustomInstructions?:      string;
	sttProvider?:                          'browser' | 'server' | 'wasm';
	sttServerUrl?:                         string;
	sttModel?:                             string;
	wasmSttModel?:                         string;
	sttLanguage?:                          string;
	sttSilenceThresholdDb?:                number;
	sttSilenceHoldMs?:                     number;
	sttTranscribeOnStop?:                  boolean;
};

// ── Runtime detection — Lis only runs in the VS Code webview ────────

/** True when the page is running inside a Tauri shell. Always false in Lis. */
export const isTauriShell    = (): boolean => false;
/** True when the page is running inside Electron. Always false in Lis. */
export const isElectronShell = (): boolean => false;
/** Generic "desktop shell" predicate (Tauri or Electron). Always false in Lis. */
export const isDesktopShell  = (): boolean => false;
/** True when running inside the VS Code extension webview. Always true here. */
export const isVSCodeRuntime = (): boolean => true;
/** True when running inside a plain web browser tab. Always false in Lis. */
export const isWebRuntime    = (): boolean => false;
/** Alias kept for OC parity. */
export const isElectronRuntime = (): boolean => false;

// ── Desktop IPC shims — none of these apply to VS Code ──────────────

/** Tauri IPC is unavailable in Lis. */
export const canUseElectronDesktopIPC = (): boolean => false;
/** Invoke a Tauri/Electron command — always returns null in Lis. */
export const invokeDesktop = async <T = unknown>(_command: string, _args?: Record<string, unknown>): Promise<T | null> => null;
/** Tauri "is local origin" check — false; Lis routes through extension host. */
export const isDesktopLocalOriginActive = (): boolean => false;
/** LAN-broadcast address for the desktop server — N/A in Lis. */
export const getDesktopLanAddress = (): string | null => null;
/** Restart the desktop app — N/A in Lis (use VS Code's Reload Window command instead). */
export const restartDesktopApp = async (): Promise<boolean> => false;
/** Tauri window-drag handle — N/A in webview (no draggable chrome). */
export const startDesktopWindowDrag = async (): Promise<boolean> => false;
/** Send a native completion notification — N/A in Lis (use VS Code notifications instead). */
export const sendAssistantCompletionNotification = async (_payload?: AssistantNotificationPayload): Promise<boolean> => false;
/** Check for desktop updates — N/A in Lis (VS Code marketplace handles extension updates). */
export const checkForDesktopUpdates = async (): Promise<UpdateInfo | null> => null;
/** Download a desktop update — N/A in Lis. */
export const downloadDesktopUpdate = async (_onProgress?: (progress: UpdateProgress) => void): Promise<boolean> => false;
/** Install a downloaded update — N/A in Lis. */
export const installDesktopUpdate = async (): Promise<boolean> => false;

// ── Native file/folder pickers — N/A in webview ─────────────────────

/** Open a native folder picker — N/A in Lis. Returns the path the caller passed in, mirroring OC's web fallback. */
export const requestDirectoryAccess = async (
	directoryPath: string,
): Promise<{ success: boolean; path?: string; projectId?: string; error?: string }> => {
	return { success: true, path: directoryPath };
};

/** Open a native file picker — N/A in Lis. */
export const requestFileAccess = async (
	_options?: { filters?: Array<{ name: string; extensions: string[] }>; defaultPath?: string },
): Promise<{ success: boolean; path?: string; error?: string }> => {
	return { success: false, error: 'Native file picker not available in VS Code webview' };
};

/** Acquire access to a directory bookmark. OC's tauri-only flow — a noop success in Lis. */
export const startAccessingDirectory = async (_directoryPath: string): Promise<{ success: boolean; error?: string }> => ({ success: true });
/** Release a directory bookmark. OC's tauri-only flow — a noop success in Lis. */
export const stopAccessingDirectory  = async (_directoryPath: string): Promise<{ success: boolean; error?: string }> => ({ success: true });

/** Embedded home directory — Lis reads this from useDirectoryStore (host-injected). */
export const getDesktopHomeDirectory = async (): Promise<string | null> => null;

// ── Open-path / open-file-in-app — wired through the extension host ─

interface OpenExternalMessage {
	type: 'host_open_external';
	path: string;
	/** Optional bundle id / app name hint. VS Code can't always honour this — it falls back to the OS default. */
	app?: string;
}

interface VsCodeApi { postMessage(msg: unknown): void }

function postToHost(msg: OpenExternalMessage): boolean {
	const w = typeof window !== 'undefined' ? window as unknown as { acquireVsCodeApi?: () => VsCodeApi } : undefined;
	const api = w?.acquireVsCodeApi?.();
	if (!api) return false;
	api.postMessage(msg);
	return true;
}

/**
 * Open a path in the OS default handler (or a specific app when supported).
 * In Lis this dispatches `host_open_external` to the extension host, which
 * resolves it through `vscode.env.openExternal` (URIs) or `revealFileInOS`
 * (paths). Returns true if the dispatch succeeded — actual success of the
 * open is opaque (VS Code's API doesn't surface it).
 */
export const openDesktopPath = async (path: string, app?: string | null): Promise<boolean> => {
	const trimmed = (path ?? '').trim();
	if (!trimmed) return false;
	return postToHost({
		type: 'host_open_external',
		path: trimmed,
		app:  typeof app === 'string' && app.trim().length > 0 ? app.trim() : undefined,
	});
};

/**
 * "Open this file with that app id" — in Lis this delegates to the same
 * extension-host hook as {@link openDesktopPath} (we can't really enforce
 * the app id since VS Code doesn't expose a chooser, but we forward the
 * hint for completeness).
 */
export const openDesktopFileInApp = async (filePath: string, appId: string, _appName: string): Promise<boolean> => {
	const trimmedFile = (filePath ?? '').trim();
	const trimmedApp  = (appId ?? '').trim();
	if (!trimmedFile || !trimmedApp) return false;
	return postToHost({ type: 'host_open_external', path: trimmedFile, app: trimmedApp });
};
