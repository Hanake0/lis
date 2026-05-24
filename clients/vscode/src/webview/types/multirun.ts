/**
 * Multi-run dialog input/output shapes — mirrors OC's `types/multirun.ts`
 * so byte-identical components (AgentManagerView, ChatGroupRunButton, etc.)
 * compile unchanged.
 */

export interface MultiRunModelSelection {
	providerID:   string;
	modelID:      string;
	displayName?: string;
	variant?:     string;
}

export interface MultiRunFileAttachment {
	mime:     string;
	filename: string;
	url:      string;
}

export interface MultiRunGroup {
	prompt: string;
	models: MultiRunModelSelection[];
}

export interface CreateMultiRunParams {
	name:                 string;
	groups:               MultiRunGroup[];
	agent?:               string;
	worktreeBaseBranch?:  string;
	isolateRuns?:         boolean;
	files?:               MultiRunFileAttachment[];
	setupCommands?:       string[];
}

export interface CreateMultiRunResult {
	groupSlug:      string;
	sessionIds:     string[];
	firstSessionId: string | null;
}

/** Carried over from the existing Lis stub so any importers still resolve. */
export interface MultiRunConfig {
	id:   string;
	name: string;
}
