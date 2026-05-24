import { useRef, useState, useEffect } from 'react';

export function useIsTextTruncated<T extends HTMLElement>(): [React.RefObject<T | null>, boolean] {
	const ref = useRef<T | null>(null);
	const [truncated, setTruncated] = useState(false);
	useEffect(() => {
		const el = ref.current;
		if (el) setTruncated(el.scrollWidth > el.clientWidth);
	}, []);
	return [ref, truncated];
}
