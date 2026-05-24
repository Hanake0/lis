import { create } from 'zustand';

import { LisRpcClient, LisRpcError } from '@/lib/lis/rpc-client';

/**
 * Git identity profile manager — Lis-native, backed by `/api/git/identities/*`
 * (Stage 10 backend). Mirrors OC's `useGitIdentitiesStore` surface so the
 * byte-identical settings pages (`GitPage`, `GitIdentityEditorDialog`,
 * `DirectoryExplorerDialog`, `GitView`) compile and behave unchanged.
 *
 * Discovered git credentials (`~/.git-credentials`, OS keychain) remain a
 * desktop-OC concept; the Lis backend does not surface them and the array is
 * always empty here. The "import this credential as a profile" UI degrades
 * to a no-op which is the accurate state.
 */

export type GitIdentityAuthType = 'ssh' | 'token';

export interface GitIdentityProfile {
	id:         string;
	name:       string;
	userName:   string;
	userEmail:  string;
	authType?:  GitIdentityAuthType;
	sshKey?:    string | null;
	host?:      string | null;
	color?:     string | null;
	icon?:      string | null;
}

export interface DiscoveredGitCredential {
	host:     string;
	username: string;
}

// ── Wire shapes (mirror Lis.Api.Schemas.Git.GitIdentityDto) ─────────────────

interface GitIdentityDto {
	id:           string;
	name:         string;
	user_name:    string;
	user_email:   string;
	auth_type:    string | null;
	ssh_key_path: string | null;
	host:         string | null;
	color:        string | null;
	icon:         string | null;
	created_at:   string;
	updated_at:   string;
}

interface GlobalGitIdentityDto {
	user_name:   string | null;
	user_email:  string | null;
	ssh_command: string | null;
}

interface DefaultGitIdentityDto {
	external_id: string | null;
}

function dtoToProfile(dto: GitIdentityDto): GitIdentityProfile {
	return {
		id:        dto.id,
		name:      dto.name,
		userName:  dto.user_name,
		userEmail: dto.user_email,
		authType:  dto.auth_type === 'ssh' || dto.auth_type === 'token' ? dto.auth_type : undefined,
		sshKey:    dto.ssh_key_path,
		host:      dto.host,
		color:     dto.color,
		icon:      dto.icon,
	};
}

function profileToWire(p: Partial<GitIdentityProfile> & { id?: string }): Record<string, unknown> {
	const out: Record<string, unknown> = {};
	if (p.id        !== undefined) out.id           = p.id;
	if (p.name      !== undefined) out.name         = p.name;
	if (p.userName  !== undefined) out.user_name    = p.userName;
	if (p.userEmail !== undefined) out.user_email   = p.userEmail;
	if (p.authType  !== undefined) out.auth_type    = p.authType;
	if (p.sshKey    !== undefined) out.ssh_key_path = p.sshKey;
	if (p.host      !== undefined) out.host         = p.host;
	if (p.color     !== undefined) out.color        = p.color;
	if (p.icon      !== undefined) out.icon         = p.icon;
	return out;
}

// ── Store ───────────────────────────────────────────────────────────────────

interface GitIdentitiesStore {
	selectedProfileId:     string | null;
	defaultGitIdentityId:  string | null;
	profiles:              GitIdentityProfile[];
	globalIdentity:        GitIdentityProfile | null;
	discoveredCredentials: DiscoveredGitCredential[];
	isLoading:             boolean;
	lastError:             string | null;

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
	lastError:             null,

	setSelectedProfile: (id) => set({ selectedProfileId: id }),

	loadProfiles: async () => {
		set({ isLoading: true });
		try {
			const dtos = await LisRpcClient.instance().listGitIdentities() as GitIdentityDto[];
			set({ profiles: dtos.map(dtoToProfile), isLoading: false, lastError: null });
			return true;
		} catch (err) {
			set({ isLoading: false, lastError: errorMessage(err) });
			return false;
		}
	},

	loadGlobalIdentity: async () => {
		try {
			const dto = await LisRpcClient.instance().getGlobalGitIdentity() as GlobalGitIdentityDto | null;

			if (!dto || (!dto.user_name && !dto.user_email)) {
				set({ globalIdentity: null });
				return true;
			}

			const profile: GitIdentityProfile = {
				id:        'global',
				name:      'Global Identity',
				userName:  dto.user_name ?? '',
				userEmail: dto.user_email ?? '',
				authType:  dto.ssh_command ? 'ssh' : undefined,
				sshKey:    dto.ssh_command ? dto.ssh_command.replace(/^ssh\s+-i\s+/, '') : null,
				color:     'info',
				icon:      'house',
			};
			set({ globalIdentity: profile });
			return true;
		} catch (err) {
			set({ globalIdentity: null, lastError: errorMessage(err) });
			return false;
		}
	},

	loadDiscoveredCredentials: async () => {
		// Lis backend has no credential discovery endpoint (it's an OC-desktop OS-keychain
		// feature). Always empty — no-op success.
		set({ discoveredCredentials: [] });
		return true;
	},

	loadDefaultGitIdentityId: async () => {
		try {
			const dto = await LisRpcClient.instance().getDefaultGitIdentity() as DefaultGitIdentityDto;
			set({ defaultGitIdentityId: dto.external_id });
			return true;
		} catch (err) {
			set({ lastError: errorMessage(err) });
			return false;
		}
	},

	setDefaultGitIdentityId: async (id) => {
		// Optimistic local update + rollback on failure.
		const previous = get().defaultGitIdentityId;
		set({ defaultGitIdentityId: id });
		try {
			await LisRpcClient.instance().setDefaultGitIdentity(id);
			return true;
		} catch (err) {
			set({ defaultGitIdentityId: previous, lastError: errorMessage(err) });
			return false;
		}
	},

	createProfile: async (profile) => {
		try {
			const dto = await LisRpcClient.instance().createGitIdentity(profileToWire({
				...profile,
				color: profile.color ?? 'keyword',
				icon:  profile.icon  ?? 'branch',
			})) as GitIdentityDto;
			set({ profiles: [...get().profiles, dtoToProfile(dto)], lastError: null });
			return true;
		} catch (err) {
			set({ lastError: errorMessage(err) });
			return false;
		}
	},

	updateProfile: async (id, updates) => {
		const previous = get().profiles.find(p => p.id === id);
		if (!previous) return false;

		// Optimistic merge locally.
		const optimistic = { ...previous, ...updates, id };
		set({ profiles: get().profiles.map(p => p.id === id ? optimistic : p) });

		try {
			const dto = await LisRpcClient.instance().updateGitIdentity(id, profileToWire(updates)) as GitIdentityDto;
			const next = dtoToProfile(dto);
			set({ profiles: get().profiles.map(p => p.id === id ? next : p), lastError: null });
			return true;
		} catch (err) {
			set({ profiles: get().profiles.map(p => p.id === id ? previous : p), lastError: errorMessage(err) });
			return false;
		}
	},

	deleteProfile: async (id) => {
		const previous = get().profiles;
		// Optimistic removal + selection cleanup.
		set({
			profiles:          previous.filter(p => p.id !== id),
			selectedProfileId: get().selectedProfileId === id ? null : get().selectedProfileId,
		});
		try {
			await LisRpcClient.instance().deleteGitIdentity(id);
			return true;
		} catch (err) {
			set({ profiles: previous, lastError: errorMessage(err) });
			return false;
		}
	},

	getProfileById: (id) => {
		const { profiles, globalIdentity } = get();
		if (id === 'global') return globalIdentity ?? undefined;
		return profiles.find(p => p.id === id);
	},

	getUnimportedCredentials: () => {
		// Same shape as OC: filter discovered credentials that haven't been imported as
		// token-based profiles. With Lis discoveredCredentials always empty, this is
		// always empty too — accurate.
		const { profiles, discoveredCredentials } = get();
		return discoveredCredentials.filter(cred =>
			!profiles.some(p => p.authType === 'token' && p.host === cred.host),
		);
	},
}));

function errorMessage(err: unknown): string {
	if (err instanceof LisRpcError) return err.message;
	if (err instanceof Error)        return err.message;
	return String(err);
}
