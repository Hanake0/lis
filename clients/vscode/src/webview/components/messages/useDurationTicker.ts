import { useState, useEffect } from 'react';

type Subscriber = (now: number) => void;

interface TickerChannel {
	subscribers: Set<Subscriber>;
	timerId:     number | null;
}

const channels = new Map<number, TickerChannel>();

function subscribeToTicker(intervalMs: number, sub: Subscriber): () => void {
	let ch = channels.get(intervalMs);
	if (!ch) {
		ch = { subscribers: new Set<Subscriber>(), timerId: null };
		channels.set(intervalMs, ch);
	}
	ch.subscribers.add(sub);
	sub(Date.now());

	if (ch.timerId === null && typeof window !== 'undefined') {
		ch.timerId = window.setInterval(() => {
			const now = Date.now();
			ch!.subscribers.forEach(listener => listener(now));
		}, intervalMs);
	}

	return () => {
		const tracked = channels.get(intervalMs);
		if (!tracked) return;
		tracked.subscribers.delete(sub);
		if (tracked.subscribers.size > 0) return;
		if (tracked.timerId !== null && typeof window !== 'undefined') {
			window.clearInterval(tracked.timerId);
		}
		channels.delete(intervalMs);
	};
}

export function useDurationTickerNow(active: boolean, intervalMs = 250): number {
	const [now, setNow] = useState(() => Date.now());

	useEffect(() => {
		if (!active) return;
		return subscribeToTicker(intervalMs, setNow);
	}, [active, intervalMs]);

	return now;
}
