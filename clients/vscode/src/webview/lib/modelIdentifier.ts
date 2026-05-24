export function parseModelIdentifier(_id: string): { provider?: string; model: string } {
	return { model: _id };
}
