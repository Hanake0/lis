export function mergeModelMetadataWithLiveModel(_metadata: unknown, _live: unknown): unknown {
	return _live ?? _metadata;
}
