/**
 * Builds the "favorites" and "recents" lists the Composer's model picker
 * renders at the top of the dropdown. OC ships per-provider `{providerID,
 * modelID}` tuples; Lis keeps favorites as plain `Set<modelId>` in the UI
 * store (provider-agnostic — same id always belongs to the same provider in
 * the catalog we ship), so we resolve providers by scanning the catalog.
 * Recents tracking isn't wired yet — `recentModelsList` is always empty.
 */
import React from 'react';
import { useConfigStore, type ProviderEntry, type ProviderModel } from '@/stores/useConfigStore';
import { useUIStore } from '@/stores/useUIStore';

export interface ModelListItem {
	provider:   ProviderEntry;
	model:      ProviderModel;
	providerID: string;
	modelID:    string;
}

export function useModelLists() {
	const providers      = useConfigStore((state: any) => state.providers as ProviderEntry[]);
	const favoriteModels = useUIStore((state: any) => state.favoriteModels as Set<string>);
	const hiddenModels   = useUIStore((state: any) => state.hiddenModels   as string[]);

	const isHidden = React.useCallback((modelID: string) => hiddenModels.includes(modelID), [hiddenModels]);

	const favoriteModelsList = React.useMemo<ModelListItem[]>(() => {
		const out: ModelListItem[] = [];
		for (const modelID of favoriteModels) {
			if (isHidden(modelID)) continue;
			for (const provider of providers) {
				const models = Array.isArray(provider.models) ? provider.models : [];
				const model  = models.find((m) => m.id === modelID);
				if (model) {
					out.push({ provider, model, providerID: provider.id, modelID });
					break;
				}
			}
		}
		return out;
	}, [favoriteModels, providers, isHidden]);

	// Recent-models tracking belongs in useUIStore but isn't wired today.
	// Returning an empty list is the shape OC's components expect.
	const recentModelsList: ModelListItem[] = React.useMemo(() => [], []);

	return { favoriteModelsList, recentModelsList };
}
