import { create } from 'zustand';
const noop: any = () => {};
const asyncNoop: any = async () => {};
export const useTerminalStore = create<any>(() => ({
	terminals: [], activeTerminalId: null, isConnected: false,
	output: [], inputHistory: [], historyIndex: -1,
	createTerminal: asyncNoop, closeTerminal: noop, setActiveTerminal: noop,
	sendInput: noop, clearOutput: noop, resize: noop,
	getTerminal: () => null, isTerminalActive: () => false,
	fetchTerminals: asyncNoop, killTerminal: asyncNoop,
	connectTerminal: asyncNoop, disconnectTerminal: noop,
}));