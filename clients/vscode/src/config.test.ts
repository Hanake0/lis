import { describe, expect, test } from 'bun:test';
import {
	DEFAULT_SERVER_URL,
	deriveApiBase,
	deriveEventsUrl,
	deriveWsUrl,
	normalizeBaseOrigin,
	readConfigFrom,
} from './config';

describe('normalizeBaseOrigin', () => {
	test('passes through a clean http origin', () => {
		expect(normalizeBaseOrigin('http://localhost:3010')).toBe('http://localhost:3010');
	});

	test('passes through a clean https origin', () => {
		expect(normalizeBaseOrigin('https://lis.example.com')).toBe('https://lis.example.com');
	});

	test('strips trailing path', () => {
		expect(normalizeBaseOrigin('http://localhost:3010/api')).toBe('http://localhost:3010');
		expect(normalizeBaseOrigin('http://localhost:3010/api/events')).toBe('http://localhost:3010');
	});

	test('converts legacy ws:// URLs to http:// base origin', () => {
		expect(normalizeBaseOrigin('ws://localhost:3010/web/ws')).toBe('http://localhost:3010');
	});

	test('converts legacy wss:// URLs to https:// base origin', () => {
		expect(normalizeBaseOrigin('wss://lis.example.com/web/ws')).toBe('https://lis.example.com');
	});

	test('preserves non-default ports', () => {
		expect(normalizeBaseOrigin('http://10.0.0.1:8080/x')).toBe('http://10.0.0.1:8080');
	});

	test('adds default protocol for bare host:port', () => {
		expect(normalizeBaseOrigin('localhost:3010')).toBe('http://localhost:3010');
	});

	test('trims surrounding whitespace', () => {
		expect(normalizeBaseOrigin('   http://localhost:3010   ')).toBe('http://localhost:3010');
	});

	test('returns empty string for empty input', () => {
		expect(normalizeBaseOrigin('')).toBe('');
		expect(normalizeBaseOrigin('   ')).toBe('');
	});

	test('returns input unchanged when unparseable', () => {
		// "::not a url" cannot be parsed by URL even with default protocol
		expect(normalizeBaseOrigin('://not a url')).toBe('://not a url');
	});

	test('is idempotent', () => {
		const once = normalizeBaseOrigin('ws://host:1/web/ws');
		expect(normalizeBaseOrigin(once)).toBe(once);
	});
});

describe('deriveWsUrl', () => {
	test('http origin → ws://…/web/ws', () => {
		expect(deriveWsUrl('http://localhost:3010')).toBe('ws://localhost:3010/web/ws');
	});

	test('https origin → wss://…/web/ws', () => {
		expect(deriveWsUrl('https://lis.example.com')).toBe('wss://lis.example.com/web/ws');
	});

	test('legacy ws URL is normalized first then re-derived', () => {
		expect(deriveWsUrl('ws://localhost:3010/web/ws')).toBe('ws://localhost:3010/web/ws');
	});

	test('empty input returns empty string', () => {
		expect(deriveWsUrl('')).toBe('');
	});
});

describe('deriveApiBase', () => {
	test('appends /api', () => {
		expect(deriveApiBase('http://localhost:3010')).toBe('http://localhost:3010/api');
		expect(deriveApiBase('https://lis.example.com')).toBe('https://lis.example.com/api');
	});

	test('empty input returns empty string', () => {
		expect(deriveApiBase('')).toBe('');
	});
});

describe('deriveEventsUrl', () => {
	test('appends /api/events', () => {
		expect(deriveEventsUrl('http://localhost:3010')).toBe('http://localhost:3010/api/events');
	});

	test('empty input returns empty string', () => {
		expect(deriveEventsUrl('')).toBe('');
	});
});

describe('readConfigFrom', () => {
	const makeSource = (values: Record<string, unknown>) => ({
		get: <T>(key: string, defaultValue: T): T => (values[key] ?? defaultValue) as T,
	});

	test('defaults: configured=false because no token', () => {
		const cfg = readConfigFrom(makeSource({}));
		expect(cfg.baseOrigin).toBe(DEFAULT_SERVER_URL);
		expect(cfg.token).toBe('');
		expect(cfg.verbose).toBe(false);
		expect(cfg.isConfigured).toBe(false);
		expect(cfg.isLegacyUrl).toBe(false);
	});

	test('explicit serverUrl + token → configured=true', () => {
		const cfg = readConfigFrom(makeSource({
			serverUrl: 'http://lis.local:9000',
			authToken: 'secret',
		}));
		expect(cfg.baseOrigin).toBe('http://lis.local:9000');
		expect(cfg.token).toBe('secret');
		expect(cfg.isConfigured).toBe(true);
		expect(cfg.isLegacyUrl).toBe(false);
	});

	test('legacy ws:// URL flagged + normalized', () => {
		const cfg = readConfigFrom(makeSource({
			serverUrl: 'ws://localhost:3010/web/ws',
			authToken: 'secret',
		}));
		expect(cfg.baseOrigin).toBe('http://localhost:3010');
		expect(cfg.isLegacyUrl).toBe(true);
	});

	test('legacy /web/ws path on http flagged too', () => {
		const cfg = readConfigFrom(makeSource({
			serverUrl: 'http://localhost:3010/web/ws',
			authToken: 'secret',
		}));
		expect(cfg.baseOrigin).toBe('http://localhost:3010');
		expect(cfg.isLegacyUrl).toBe(true);
	});

	test('verbose flag passes through', () => {
		const cfg = readConfigFrom(makeSource({ verboseLogging: true }));
		expect(cfg.verbose).toBe(true);
	});

	test('empty serverUrl falls back to default', () => {
		const cfg = readConfigFrom(makeSource({ serverUrl: '' }));
		expect(cfg.baseOrigin).toBe(DEFAULT_SERVER_URL);
	});
});
