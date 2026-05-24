export const opencodeClient = {
	getSdkClient: () => ({
		session: {
			create: async () => ({}),
			delete: async () => {},
			list: async () => [],
			get: async () => ({}),
			shell: async () => {},
			chat: async () => {},
			abort: async () => {},
		},
	}),
	getDirectory: () => null as string | null,
};
