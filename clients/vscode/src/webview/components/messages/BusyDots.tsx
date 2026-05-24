import { cn } from '../../lib/utils';

const DOT_DELAYS_MS = [0, 200, 400] as const;

export function BusyDots({ className }: { className?: string }) {
	return (
		<>
			{' '}
			<span className={cn('inline-flex', className)} aria-hidden="true">
				{DOT_DELAYS_MS.map((delay) => (
					<span
						key={delay}
						style={{
							animation: 'oc-busy-pulse 1.2s ease-in-out infinite',
							animationDelay: `${delay}ms`,
						}}
					>
						.
					</span>
				))}
			</span>
		</>
	);
}
