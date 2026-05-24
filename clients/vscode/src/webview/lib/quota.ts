export const QUOTA_PROVIDERS: Array<{ id: string; name: string }> = [];

export function formatQuotaValueLabel(_label: string | undefined, _percent: number): string { return '-'; }
export function formatQuotaResetLabel(_resetAt: number | null, _formatted?: string): string { return ''; }
export function formatWindowLabel(_label: string): string { return _label; }
export function calculatePace(_usedPercent: number, _resetAt: number | null, _windowSeconds?: number, _label?: string): unknown { return null; }
export function calculateExpectedUsagePercent(_elapsedRatio: number): number { return 0; }
