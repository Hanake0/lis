const DARK_THEME = {
	metadata: { id: 'dark', name: 'Dark', variant: 'dark', family: 'default' },
	isDark: true,
	colors: {},
};
const LIGHT_THEME = {
	metadata: { id: 'light', name: 'Light', variant: 'light', family: 'default' },
	isDark: false,
	colors: {},
};
export const themes: unknown[] = [LIGHT_THEME, DARK_THEME];
export function getDefaultTheme(isDark?: boolean): any { return isDark ? DARK_THEME : LIGHT_THEME; }
