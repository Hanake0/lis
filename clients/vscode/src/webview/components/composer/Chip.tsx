import { useState, useEffect, useRef, type ReactNode } from 'react';
import { cn } from '../../lib/utils';
import { Icon } from '../icon/Icon';

interface Props {
	label:    string;
	dirty?:   boolean;
	children: ReactNode;
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
		<div ref={rootRef} className="relative inline-flex">
			<button
				type="button"
				onClick={() => setOpen(v => !v)}
				className={cn(
					'flex items-center gap-1 h-7 px-2 rounded-md typography-meta font-medium transition-colors',
					'hover:bg-[var(--interactive-hover)]',
					open && 'bg-[var(--interactive-active)]',
				)}
				style={{
					color: dirty ? 'var(--primary)' : 'var(--foreground)',
				}}
			>
				<span className="truncate max-w-[10rem]">{label}</span>
				<Icon
					name={open ? 'arrow-up-s' : 'arrow-down-s'}
					className="h-3.5 w-3.5 flex-shrink-0 opacity-60"
				/>
			</button>
			{open && (
				<div
					className="absolute left-0 bottom-full mb-1.5 z-20 min-w-[15rem] rounded-xl p-1.5"
					style={{
						background: 'var(--popover)',
						color:      'var(--popover-foreground)',
						border:     '1px solid var(--border)',
						boxShadow:  '0 4px 14px rgba(0,0,0,0.2), 0 12px 32px rgba(0,0,0,0.25)',
					}}
				>
					{children}
				</div>
			)}
		</div>
	);
}
