import type { ProjectEntry } from '@/lib/api/types';

/**
 * Project icon + colour metadata. Ported byte-identical from OC so the
 * sidebar project list renders the same options in the icon picker, and
 * existing projects pick the same CSS variables when displayed.
 *
 * `getProjectIconImageUrl` is the one place this file talks to the backend —
 * the URL points at the Lis `/api/projects/{id}/icon` endpoint (already wired
 * in ProjectsController), which serves the user-uploaded icon blob.
 */

type ThemeVariant = 'light' | 'dark';

export interface ProjectIcon {
	key:   string;
	Icon:  string;  // Iconify / lucide icon id — consumers map this to their component
	label: string;
}

export const PROJECT_ICONS: ProjectIcon[] = [
	{ key: 'code',      Icon: 'code-box',     label: 'Code' },
	{ key: 'terminal',  Icon: 'terminal-box', label: 'Terminal' },
	{ key: 'rocket',    Icon: 'rocket',       label: 'Rocket' },
	{ key: 'flask',     Icon: 'flask',        label: 'Lab' },
	{ key: 'gamepad',   Icon: 'gamepad',      label: 'Game' },
	{ key: 'briefcase', Icon: 'briefcase',    label: 'Work' },
	{ key: 'home',      Icon: 'home',         label: 'Home' },
	{ key: 'globe',     Icon: 'global',       label: 'Web' },
	{ key: 'leaf',      Icon: 'leaf',         label: 'Nature' },
	{ key: 'shield',    Icon: 'shield',       label: 'Security' },
	{ key: 'palette',   Icon: 'palette',      label: 'Design' },
	{ key: 'server',    Icon: 'server',       label: 'Server' },
	{ key: 'phone',     Icon: 'smartphone',   label: 'Mobile' },
	{ key: 'database',  Icon: 'database-2',   label: 'Data' },
	{ key: 'lightbulb', Icon: 'lightbulb',    label: 'Idea' },
	{ key: 'music',     Icon: 'music',        label: 'Music' },
	{ key: 'camera',    Icon: 'camera',       label: 'Media' },
	{ key: 'book',      Icon: 'book-open',    label: 'Docs' },
	{ key: 'heart',     Icon: 'heart',        label: 'Favorite' },
];

export const PROJECT_ICON_MAP: Record<string, string> = Object.fromEntries(
	PROJECT_ICONS.map(i => [i.key, i.Icon]),
);

export interface ProjectColor {
	key:    string;
	label:  string;
	cssVar: string;
}

export const PROJECT_COLORS: ProjectColor[] = [
	{ key: 'keyword',  label: 'Purple', cssVar: 'var(--syntax-keyword)' },
	{ key: 'string',   label: 'Green',  cssVar: 'var(--syntax-string)' },
	{ key: 'number',   label: 'Pink',   cssVar: 'var(--syntax-number)' },
	{ key: 'type',     label: 'Gold',   cssVar: 'var(--syntax-type)' },
	{ key: 'constant', label: 'Cyan',   cssVar: 'var(--syntax-constant)' },
	{ key: 'comment',  label: 'Muted',  cssVar: 'var(--syntax-comment)' },
	{ key: 'error',    label: 'Red',    cssVar: 'var(--status-error)' },
	{ key: 'primary',  label: 'Blue',   cssVar: 'var(--primary)' },
	{ key: 'success',  label: 'Green',  cssVar: 'var(--status-success)' },
];

export const PROJECT_COLOR_MAP: Record<string, string> = Object.fromEntries(
	PROJECT_COLORS.map(c => [c.key, c.cssVar]),
);

/**
 * Build the URL the sidebar uses to fetch a project's icon blob. Points at
 * Lis's `GET /api/projects/{id}/icon` — backend already serves it (see
 * `ProjectsController.GetIcon`). Returns null when no icon is uploaded.
 */
export function getProjectIconImageUrl(
	project: Pick<ProjectEntry, 'id'> & { icon_image_updated_at?: string | null },
	options?: { themeVariant?: ThemeVariant; iconColor?: string; baseOrigin?: string },
): string | null {
	const updatedAt = project.icon_image_updated_at;
	if (!updatedAt) return null;

	const params = new URLSearchParams({ v: updatedAt });
	if (typeof options?.iconColor === 'string' && options.iconColor.trim()) {
		params.set('iconColor', options.iconColor.trim());
	}
	if (options?.themeVariant === 'light' || options?.themeVariant === 'dark') {
		params.set('theme', options.themeVariant);
	}

	const base = options?.baseOrigin?.replace(/\/+$/, '') ?? '';
	return `${base}/api/projects/${encodeURIComponent(project.id)}/icon?${params.toString()}`;
}
