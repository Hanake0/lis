import { create } from 'zustand';
const noop: any = () => {};
const asyncNoop: any = async () => {};
export const useGitIdentitiesStore = create<any>(() => ({
	identities: [], currentIdentity: null, isLoading: false, selectedIdentity: null, identityMap: {},
	fetchIdentities: noop, setIdentity: noop, selectIdentity: noop,
	clearSelection: noop, getIdentityForDirectory: () => null,
}));