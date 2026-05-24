export function isMessageFresh(_ts: number): boolean { return true; }
const detectorInstance = {
	recordSessionStart: () => {},
	isMessageFresh: () => true,
	recordMessageSeen: () => {},
	getLastSeenTimestamp: () => 0,
	shouldAnimateMessage: () => false,
	markMessageAnimated: () => {},
};
export const MessageFreshnessDetector = {
	getInstance: () => detectorInstance,
};
