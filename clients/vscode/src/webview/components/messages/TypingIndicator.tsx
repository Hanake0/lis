export function TypingIndicator() {
	return (
		<div className="chat-column my-1.5">
			<div className="flex items-center gap-1" aria-label="Lis is typing">
				{([0, 200, 400] as const).map((delay) => (
					<span
						key={delay}
						className="w-1.5 h-1.5 rounded-full"
						style={{
							background: 'var(--muted-foreground)',
							animation: 'oc-busy-pulse 1.2s ease-in-out infinite',
							animationDelay: `${delay}ms`,
						}}
					/>
				))}
			</div>
		</div>
	);
}
