import { beforeEach, describe, expect, test } from 'bun:test';

import { useProjectsStore } from '@/stores/useProjectsStore';
import { resolveProjectForSessionDirectory } from './projectResolution';

function project(over: Partial<{ id: string; path: string }> = {}): unknown {
	return {
		id:                'p1',
		path:              '/repo',
		label:             'Repo',
		added_at:          '2026-01-01T00:00:00Z',
		created_at:        '2026-01-01T00:00:00Z',
		updated_at:        '2026-01-01T00:00:00Z',
		sidebar_collapsed: false,
		sort_index:        0,
		...over,
	};
}

describe('resolveProjectForSessionDirectory', () => {
	beforeEach(() => {
		useProjectsStore.setState({
			projects: [
				project({ id: 'p1', path: '/Users/o/repo'   }),
				project({ id: 'p2', path: '/Users/o/other'  }),
				project({ id: 'p3', path: 'C:\\dev\\proj'   }),
			] as never,
		});
	});

	test('returns null for falsy directory', () => {
		expect(resolveProjectForSessionDirectory(null)).toBeNull();
		expect(resolveProjectForSessionDirectory('')).toBeNull();
		expect(resolveProjectForSessionDirectory('   ')).toBeNull();
	});

	test('returns null when no project matches', () => {
		expect(resolveProjectForSessionDirectory('/unknown')).toBeNull();
	});

	test('exact path match returns the project id', () => {
		expect(resolveProjectForSessionDirectory('/Users/o/repo')).toBe('p1');
	});

	test('trailing slash is ignored', () => {
		expect(resolveProjectForSessionDirectory('/Users/o/repo/')).toBe('p1');
	});

	test('backslashes (Windows) are normalised to forward slashes', () => {
		expect(resolveProjectForSessionDirectory('C:/dev/proj')).toBe('p3');
		expect(resolveProjectForSessionDirectory('C:\\dev\\proj')).toBe('p3');
		expect(resolveProjectForSessionDirectory('c:\\dev\\proj')).toBe('p3'); // drive letter case insensitive
	});
});
