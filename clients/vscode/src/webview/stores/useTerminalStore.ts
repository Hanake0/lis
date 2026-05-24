import { create } from 'zustand';
const noop: any = () => {};
export const useTerminalStore = create<any>(() => ({
	terminals: [], sessions: [], activeTerminalId: null, isConnected: false,
	output: [], inputHistory: [], historyIndex: -1, projectActionRuns: [],
	hasHydrated: true,
	createTerminal: noop, closeTerminal: noop, setActiveTerminal: noop,
	sendInput: noop, clearOutput: noop, resize: noop,
	getTerminal: () => null, isTerminalActive: () => false,
	fetchTerminals: noop, killTerminal: noop, connectTerminal: noop, disconnectTerminal: noop,
	ensureDirectory: noop, setTabLabel: noop, setTabIconKey: noop, setActiveTab: noop,
	setConnecting: noop, setTabSessionId: noop, setTabPreviewUrl: noop,
	setProjectActionRun: noop, updateProjectActionRunStatus: noop, removeProjectActionRun: noop,
	createTab: noop, closeTab: noop, setTabLifecycle: noop, appendToBuffer: noop, clearBuffer: noop,
}));