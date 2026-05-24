export interface Session {
	id: string;
	title?: string;
	parentID?: string;
	directory?: string | null;
	createdAt?: number;
	updatedAt?: number;
}

export interface Message {
	id: string;
	role: 'user' | 'assistant' | 'tool' | 'system';
	content?: string;
	text?: string;
	createdAt?: number;
	time?: { completed?: number };
	providerID?: string;
	modelID?: string;
	tokens?: {
		input: number;
		output: number;
		reasoning: number;
		cache: { read: number; write: number };
	};
}

export interface Part {
	id?: string;
	type: 'text' | 'tool' | 'file' | 'thinking';
	text?: string;
	content?: string;
	toolName?: string;
	toolArgs?: string;
	toolResult?: string;
}

export type TextPart = Part & { type: 'text'; text: string };

export interface Provider {
	id: string;
	name: string;
	models: Array<{ id: string; name?: string }>;
}

export interface Agent {
	id: string;
	name: string;
	model?: string;
	provider?: string;
}
