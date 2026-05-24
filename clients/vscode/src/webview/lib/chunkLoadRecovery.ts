import React from 'react';

export function lazyWithChunkRecovery<T extends React.ComponentType<unknown>>(
	factory: () => Promise<{ default: T }>
): React.LazyExoticComponent<T> {
	return React.lazy(factory);
}
