import { create } from 'zustand';
const noop: any = () => {};

export const useSessionWorktreeStore = create<any>(() => ({
	attachments: new Map(),
	getAttachment: (_sessionId: string) => null,
	setAttachment: noop,
	removeAttachment: noop,
}));
