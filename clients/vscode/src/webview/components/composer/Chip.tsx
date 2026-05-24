import { useState, useEffect, useRef } from 'preact/hooks';
import type { ComponentChildren } from 'preact';

interface Props {
	label: string;
	dirty?: boolean;
	children: ComponentChildren;
}

export function Chip({ label, dirty, children }: Props) {
	const [open, setOpen] = useState(false);
	const rootRef = useRef<HTMLDivElement>(null);

	useEffect(() => {
		if (!open) return;
		function onDown(e: PointerEvent) {
			if (rootRef.current && !rootRef.current.contains(e.target as Node)) setOpen(false);
		}
		function onKey(e: KeyboardEvent) {
			if (e.key === 'Escape') setOpen(false);
		}
		document.addEventListener('pointerdown', onDown);
		document.addEventListener('keydown', onKey);
		return () => {
			document.removeEventListener('pointerdown', onDown);
			document.removeEventListener('keydown', onKey);
		};
	}, [open]);

	return (
		<div class="chip-wrap" ref={rootRef}>
			<button
				type="button"
				class={`chip${dirty ? ' chip--dirty' : ''}${open ? ' chip--open' : ''}`}
				onClick={() => setOpen(v => !v)}
			>
				<span class="chip-label">{label}</span>
				<span class="chip-arrow">▾</span>
			</button>
			{open && (
				<div class="dropdown-panel">
					{children}
				</div>
			)}
		</div>
	);
}
