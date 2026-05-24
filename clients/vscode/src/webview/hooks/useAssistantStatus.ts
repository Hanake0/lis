const IDLE = {
	working: {
		isWorking: false, statusText: '', isGenericStatus: false,
		isWaitingForPermission: false, wasAborted: false, abortActive: false,
		retryInfo: null,
	},
};
export function useAssistantStatus(..._args: any[]): any {
	return IDLE;
}
