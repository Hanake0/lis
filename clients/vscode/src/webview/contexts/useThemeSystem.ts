const DEFAULT_THEME = {
	id: 'dark',
	name: 'Dark',
	isDark: true,
	colors: {
		background: 'var(--background)',
		foreground: 'var(--foreground)',
		surface: {
			background: 'var(--surface-background)',
			foreground: 'var(--surface-foreground)',
			muted: 'var(--surface-muted)',
			mutedForeground: 'var(--surface-mutedForeground)',
			elevated: 'var(--surface-elevated)',
			elevatedForeground: 'var(--surface-elevatedForeground)',
		},
		primary: { base: 'var(--primary)' },
		border: 'var(--border)',
	},
	metadata: {
		variant: 'dark',
		family: 'default',
	},
};

export function useThemeSystem() {
	return {
		theme: 'dark',
		currentTheme: DEFAULT_THEME,
		themes: [DEFAULT_THEME],
		setTheme: (_t: string) => {},
		resolvedTheme: DEFAULT_THEME,
	};
}
export function useOptionalThemeSystem() { return useThemeSystem(); }
