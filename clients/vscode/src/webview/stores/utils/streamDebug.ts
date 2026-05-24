export function streamDebugEnabled(): boolean { return false; }
export function streamPerfCount(..._args: any[]): any { return null; }
export function streamPerfMeasure(_label: string, fn: () => any): any { return fn(); }
export function streamPerfObserve(..._args: any[]): any { return null; }
