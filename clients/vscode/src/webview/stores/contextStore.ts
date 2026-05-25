/**
 * Per-chat composer-selection store — the bit of OC's contextStore that
 * actually feeds the dialogs (GitHubIssuePicker, NewWorktree) and the
 * sticky chrome (ChatMessage agent-pill, StatusChip). We DON'T port the
 * client-side context-usage calculation: that lives server-side in
 * `GET /api/chats/{id}/context` and arrives through useChatStore.contextData.
 *
 * State is persisted to localStorage so a webview reload doesn't lose the
 * user's pinned model + agent for a chat. Map structures are JSON-encoded
 * via tuple arrays — Zustand's `persist` middleware doesn't natively serialise
 * ES6 Maps, so we own the serializer.
 */
import { create } from 'zustand';

type SessionId = string;
type AgentName = string;

interface ModelKey { providerId: string; modelId: string; }

type EditPermissionMode = 'ask' | 'allow' | 'full';

const STORAGE_KEY = 'lis.contextStore.v1';

interface PersistedShape {
	sessionModel:        [SessionId, ModelKey][];
	sessionAgent:        [SessionId, AgentName][];
	sessionAgentModel:   [SessionId, [AgentName, ModelKey][]][];
	sessionAgentVariant: [SessionId, [AgentName, [string, string][]][]][];
	agentEditModes:      [SessionId, [AgentName, EditPermissionMode][]][];
}

function readPersisted(): PersistedShape {
	if (typeof window === 'undefined') {
		return { sessionModel: [], sessionAgent: [], sessionAgentModel: [], sessionAgentVariant: [], agentEditModes: [] };
	}
	try {
		const raw = window.localStorage.getItem(STORAGE_KEY);
		if (!raw) return { sessionModel: [], sessionAgent: [], sessionAgentModel: [], sessionAgentVariant: [], agentEditModes: [] };
		const parsed = JSON.parse(raw) as Partial<PersistedShape>;
		return {
			sessionModel:        Array.isArray(parsed.sessionModel)        ? parsed.sessionModel        : [],
			sessionAgent:        Array.isArray(parsed.sessionAgent)        ? parsed.sessionAgent        : [],
			sessionAgentModel:   Array.isArray(parsed.sessionAgentModel)   ? parsed.sessionAgentModel   : [],
			sessionAgentVariant: Array.isArray(parsed.sessionAgentVariant) ? parsed.sessionAgentVariant : [],
			agentEditModes:      Array.isArray(parsed.agentEditModes)      ? parsed.agentEditModes      : [],
		};
	} catch {
		return { sessionModel: [], sessionAgent: [], sessionAgentModel: [], sessionAgentVariant: [], agentEditModes: [] };
	}
}

function flushPersisted(state: ContextStore) {
	if (typeof window === 'undefined') return;
	try {
		const shape: PersistedShape = {
			sessionModel:        [...state.sessionModelSelections.entries()],
			sessionAgent:        [...state.sessionAgentSelections.entries()],
			sessionAgentModel:   [...state.sessionAgentModelSelections.entries()].map(([k, v]) => [k, [...v.entries()]]),
			sessionAgentVariant: [...state.sessionAgentModelVariantSelections.entries()].map(
				([k, v]) => [k, [...v.entries()].map(([a, m]) => [a, [...m.entries()]])],
			),
			agentEditModes:      [...state.sessionAgentEditModes.entries()].map(([k, v]) => [k, [...v.entries()]]),
		};
		window.localStorage.setItem(STORAGE_KEY, JSON.stringify(shape));
	} catch {
		// Quota exceeded / privacy-blocked storage — the in-memory state is
		// still authoritative for this session.
	}
}

interface ContextStore {
	sessionModelSelections:             Map<SessionId, ModelKey>;
	sessionAgentSelections:             Map<SessionId, AgentName>;
	sessionAgentModelSelections:        Map<SessionId, Map<AgentName, ModelKey>>;
	sessionAgentModelVariantSelections: Map<SessionId, Map<AgentName, Map<string, string>>>;
	currentAgentContext:                Map<SessionId, AgentName>;
	sessionAgentEditModes:              Map<SessionId, Map<AgentName, EditPermissionMode>>;
	hasHydrated: boolean;

	saveSessionModelSelection: (sessionId: string, providerId: string, modelId: string) => void;
	getSessionModelSelection:  (sessionId: string) => ModelKey | null;

	saveSessionAgentSelection: (sessionId: string, agentName: string) => void;
	getSessionAgentSelection:  (sessionId: string) => string | null;

	saveAgentModelForSession: (sessionId: string, agentName: string, providerId: string, modelId: string) => void;
	getAgentModelForSession:  (sessionId: string, agentName: string) => ModelKey | null;

	saveAgentModelVariantForSession: (sessionId: string, agentName: string, providerId: string, modelId: string, variant: string | undefined) => void;
	getAgentModelVariantForSession:  (sessionId: string, agentName: string, providerId: string, modelId: string) => string | undefined;

	getCurrentAgent: (sessionId: string) => string | undefined;

	getSessionAgentEditMode:    (sessionId: string, agentName: string | undefined, defaultMode?: EditPermissionMode) => EditPermissionMode;
	toggleSessionAgentEditMode: (sessionId: string, agentName: string | undefined, defaultMode?: EditPermissionMode) => void;
	setSessionAgentEditMode:    (sessionId: string, agentName: string | undefined, mode: EditPermissionMode, defaultMode?: EditPermissionMode) => void;
}

const EDIT_PERMISSION_SEQUENCE: EditPermissionMode[] = ['ask', 'allow', 'full'];
const GLOBAL_EDIT_MODE_SESSION_ID = '__global__';

const persisted = readPersisted();

export const useContextStore = create<ContextStore>((set, get) => ({
	sessionModelSelections:             new Map(persisted.sessionModel),
	sessionAgentSelections:             new Map(persisted.sessionAgent),
	sessionAgentModelSelections:        new Map(persisted.sessionAgentModel.map(([k, v]) => [k, new Map(v)])),
	sessionAgentModelVariantSelections: new Map(persisted.sessionAgentVariant.map(([k, v]) => [k, new Map(v.map(([a, m]) => [a, new Map(m)]))])),
	currentAgentContext:                new Map(persisted.sessionAgent),
	sessionAgentEditModes:              new Map(persisted.agentEditModes.map(([k, v]) => [k, new Map(v)])),
	hasHydrated: true,

	saveSessionModelSelection: (sessionId, providerId, modelId) => {
		set((state) => {
			const next = new Map(state.sessionModelSelections);
			next.set(sessionId, { providerId, modelId });
			return { sessionModelSelections: next };
		});
		flushPersisted(get());
	},

	getSessionModelSelection: (sessionId) => get().sessionModelSelections.get(sessionId) ?? null,

	saveSessionAgentSelection: (sessionId, agentName) => {
		set((state) => {
			const sel = new Map(state.sessionAgentSelections);
			sel.set(sessionId, agentName);
			const ctx = new Map(state.currentAgentContext);
			ctx.set(sessionId, agentName);
			return { sessionAgentSelections: sel, currentAgentContext: ctx };
		});
		flushPersisted(get());
	},

	getSessionAgentSelection: (sessionId) => get().sessionAgentSelections.get(sessionId) ?? null,

	saveAgentModelForSession: (sessionId, agentName, providerId, modelId) => {
		set((state) => {
			const sessions = new Map(state.sessionAgentModelSelections);
			const agents = new Map(sessions.get(sessionId) ?? []);
			agents.set(agentName, { providerId, modelId });
			sessions.set(sessionId, agents);
			return { sessionAgentModelSelections: sessions };
		});
		flushPersisted(get());
	},

	getAgentModelForSession: (sessionId, agentName) =>
		get().sessionAgentModelSelections.get(sessionId)?.get(agentName) ?? null,

	saveAgentModelVariantForSession: (sessionId, agentName, providerId, modelId, variant) => {
		set((state) => {
			const sessions = new Map(state.sessionAgentModelVariantSelections);
			const agents   = new Map(sessions.get(sessionId) ?? []);
			const models   = new Map(agents.get(agentName)   ?? []);
			const key = `${providerId}/${modelId}`;
			if (variant === undefined) {
				models.delete(key);
				if (models.size === 0) {
					agents.delete(agentName);
				} else {
					agents.set(agentName, models);
				}
			} else {
				models.set(key, variant);
				agents.set(agentName, models);
			}
			if (agents.size === 0) sessions.delete(sessionId);
			else                    sessions.set(sessionId, agents);
			return { sessionAgentModelVariantSelections: sessions };
		});
		flushPersisted(get());
	},

	getAgentModelVariantForSession: (sessionId, agentName, providerId, modelId) =>
		get().sessionAgentModelVariantSelections.get(sessionId)?.get(agentName)?.get(`${providerId}/${modelId}`),

	getCurrentAgent: (sessionId) => get().currentAgentContext.get(sessionId),

	getSessionAgentEditMode: (sessionId, agentName, defaultMode = 'ask') => {
		const session = get().sessionAgentEditModes.get(sessionId);
		if (session && agentName) {
			const mode = session.get(agentName);
			if (mode) return mode;
		}
		// Global override for the per-agent-undefined case.
		const globalScope = get().sessionAgentEditModes.get(GLOBAL_EDIT_MODE_SESSION_ID);
		const globalMode  = globalScope?.get('*');
		return globalMode ?? defaultMode;
	},

	setSessionAgentEditMode: (sessionId, agentName, mode, _defaultMode = 'ask') => {
		set((state) => {
			const sessions = new Map(state.sessionAgentEditModes);
			const target   = agentName ? sessionId : GLOBAL_EDIT_MODE_SESSION_ID;
			const key      = agentName ?? '*';
			const agents   = new Map(sessions.get(target) ?? []);
			agents.set(key, mode);
			sessions.set(target, agents);
			return { sessionAgentEditModes: sessions };
		});
		flushPersisted(get());
	},

	toggleSessionAgentEditMode: (sessionId, agentName, defaultMode = 'ask') => {
		const current = get().getSessionAgentEditMode(sessionId, agentName, defaultMode);
		const idx     = EDIT_PERMISSION_SEQUENCE.indexOf(current);
		const next    = EDIT_PERMISSION_SEQUENCE[(idx + 1) % EDIT_PERMISSION_SEQUENCE.length];
		get().setSessionAgentEditMode(sessionId, agentName, next, defaultMode);
	},
}));
