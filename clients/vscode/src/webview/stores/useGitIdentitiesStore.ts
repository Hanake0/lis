import { create } from 'zustand';

/**
 * Git identity profile manager. In OC's desktop runtime this is wired to the
 * OS keychain + a local profile registry, and the user can pick which
 * identity is used per-project.
 *
 * The Lis backend does not currently model git identities — Stage 5 (Git +
 * Worktrees) covers repo operations but not identity provisioning. So this
 * store is a typed no-op that compiles cleanly against the byte-identical OC
 * consumers (DirectoryExplorerDialog, GitView, GitPage, GitIdentityEditorDialog)
 * but never reports any profiles. Adding real identity management is a
 * follow-up that requires:
 *
 *   - A `GitIdentityEntity` in `Lis.Persistence`.
 *   - REST endpoints under `/api/git/identities`.
 *   - Wiring through `IGitService.AttributeIdentityAsync` etc.
 *
 * Until then, the UI degrades to "no profiles configured", which is the
 * accurate state.
 */

export type GitIdentityAuthType = 'ssh' | 'token';

export interface GitIdentityProfile {
	id:        string;
	name:      string;
	userName:  string;
	userEmail: string;
	authType?: GitIdentityAuthType;
	sshKey?:   string | null;
	host?:     string | null;
	color?:    string | null;
	icon?:     string | null;
}

export interface DiscoveredGitCredential {
	host:     string;
	username: string;
}

interface GitIdentitiesStore {
	selectedProfileId:     string | null;
	defaultGitIdentityId:  string | null;
	profiles:              GitIdentityProfile[];
	globalIdentity:        GitIdentityProfile | null;
	discoveredCredentials: DiscoveredGitCredential[];
	isLoading:             boolean;

	setSelectedProfile:        (id: string | null) => void;
	loadProfiles:              () => Promise<boolean>;
	loadGlobalIdentity:        () => Promise<boolean>;
	loadDiscoveredCredentials: () => Promise<boolean>;
	loadDefaultGitIdentityId:  () => Promise<boolean>;
	setDefaultGitIdentityId:   (id: string | null) => Promise<boolean>;

	createProfile:           (profile: Omit<GitIdentityProfile, 'id'> & { id?: string }) => Promise<boolean>;
	updateProfile:           (id: string, updates: Partial<GitIdentityProfile>) => Promise<boolean>;
	deleteProfile:           (id: string) => Promise<boolean>;
	getProfileById:          (id: string) => GitIdentityProfile | undefined;
	getUnimportedCredentials: () => DiscoveredGitCredential[];
}

export const useGitIdentitiesStore = create<GitIdentitiesStore>((set, get) => ({
	selectedProfileId:     null,
	defaultGitIdentityId:  null,
	profiles:              [],
	globalIdentity:        null,
	discoveredCredentials: [],
	isLoading:             false,

	setSelectedProfile: (id) => set({ selectedProfileId: id }),

	// All loaders return `false` (not-supported); consumers expect a boolean
	// success indicator and gracefully fall back to "no profiles configured".
	loadProfiles:              async () => false,
	loadGlobalIdentity:        async () => false,
	loadDiscoveredCredentials: async () => false,
	loadDefaultGitIdentityId:  async () => false,
	setDefaultGitIdentityId:   async () => false,

	createProfile: async () => false,
	updateProfile: async () => false,
	deleteProfile: async () => false,

	getProfileById:          (id) => get().profiles.find(p => p.id === id),
	getUnimportedCredentials: () => [],
}));
