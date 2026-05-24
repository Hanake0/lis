export interface QuestionRequest {
	id: string;
	sessionId: string;
	question: string;
	options?: string[];
}
