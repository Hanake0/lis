export interface ModelMetadata {
	id: string;
	name?: string;
	limit?: { context?: number; output?: number };
}

export interface UsageWindow {
	usedPercent: number;
	remainingPercent: number;
	resetAt: number | null;
	resetAtFormatted?: string;
	resetAfterFormatted?: string;
	windowSeconds?: number;
	valueLabel?: string;
}
