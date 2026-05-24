export interface QuestionOption {
	label: string;
	description?: string;
}

export interface QuestionItem {
	question: string;
	header?: string;
	options: QuestionOption[];
	multiple?: boolean;
}

export type QuestionInfo = QuestionItem;

export interface QuestionRequest {
	id: string;
	sessionID: string;
	questions: QuestionItem[];
}
