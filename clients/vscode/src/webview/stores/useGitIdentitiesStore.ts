import { create } from 'zustand';
const noop: any = () => {};
export const useGitIdentitiesStore = create<any>(() => ({
	identities: [], currentIdentity: null, isLoading: false, selectedIdentity: null,
	identityMap: {}, profiles: [], globalIdentity: null, defaultGitIdentityId: null,
	fetchIdentities: noop, setIdentity: noop, selectIdentity: noop,
	clearSelection: noop, getIdentityForDirectory: () => null,
	getProfileById: () => null, createProfile: noop, updateProfile: noop,
	deleteProfile: noop, loadDefaultGitIdentityId: noop, loadProfiles: noop, loadGlobalIdentity: noop,
}));