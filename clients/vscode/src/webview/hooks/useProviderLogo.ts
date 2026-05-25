import { useState, useCallback, useEffect } from 'react';

type LogoSource = 'local' | 'remote' | 'none';

interface UseProviderLogoReturn {
	src: string | null;
	onError: () => void;
	hasLogo: boolean;
}

// Lis ships no bundled provider logos today — every brand we'd want is on
// models.dev and the webview CSP allows `img-src https:`. The local map is
// kept as the same shape OC uses so future commits can drop SVG files in
// `assets/provider-logos/` and wire them with a static import block here
// (esbuild doesn't support Vite's `import.meta.glob`).
const LOCAL_PROVIDER_LOGO_MAP = new Map<string, string>();

const LOGO_ALIAS = new Map<string, string>([
	['codex',         'openai'],
	['chatgpt',       'openai'],
	['claude',        'anthropic'],
	['gemini',        'google'],
	['evroc-ai',      'evroc'],
	['evrocai',       'evroc'],
	['ollama-cloud',  'ollama'],
	['wafer-ai',      'wafer.ai'],
	['wafer',         'wafer.ai'],
]);

const normalizeProviderId = (providerId: string | null | undefined) => {
	return (providerId ?? '')
		.toLowerCase()
		.trim()
		.replace(/^models\./, '')
		.replace(/^provider\./, '')
		.replace(/\s+/g, '-');
};

const buildLogoCandidates = (providerId: string | null | undefined) => {
	const normalized = normalizeProviderId(providerId);
	if (!normalized) return [] as string[];

	const compact = normalized.replace(/[^a-z0-9_\-./:]/g, '');
	const primary = compact.split(/[/:]/)[0] || compact;
	const candidates = [LOGO_ALIAS.get(compact), LOGO_ALIAS.get(primary), compact, primary]
		.filter((value): value is string => Boolean(value && value.length > 0));

	return [...new Set(candidates)];
};

export function useProviderLogo(providerId: string | null | undefined): UseProviderLogoReturn {
	const candidates = buildLogoCandidates(providerId);
	const localResolvedId  = candidates.find((c) => LOCAL_PROVIDER_LOGO_MAP.has(c)) ?? null;
	const remoteResolvedId = candidates[0] ?? null;
	const hasLocalLogo     = Boolean(localResolvedId);
	const localLogoSrc     = localResolvedId ? LOCAL_PROVIDER_LOGO_MAP.get(localResolvedId) ?? null : null;

	const [source, setSource] = useState<LogoSource>(hasLocalLogo ? 'local' : 'remote');

	useEffect(() => {
		setSource(hasLocalLogo ? 'local' : 'remote');
	}, [hasLocalLogo, localResolvedId, remoteResolvedId]);

	const handleError = useCallback(() => {
		setSource((current) => (current === 'local' && hasLocalLogo ? 'remote' : 'none'));
	}, [hasLocalLogo]);

	if (!localResolvedId && !remoteResolvedId) return { src: null, onError: handleError, hasLogo: false };

	if (source === 'local'  && localLogoSrc)     return { src: localLogoSrc,                                              onError: handleError, hasLogo: true };
	if (source === 'remote' && remoteResolvedId) return { src: `https://models.dev/logos/${remoteResolvedId}.svg`,        onError: handleError, hasLogo: true };

	return { src: null, onError: handleError, hasLogo: false };
}
