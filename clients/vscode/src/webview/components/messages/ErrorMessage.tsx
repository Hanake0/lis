export function ErrorMessage({ text }: { text: string }) {
	return (
		<div className="chat-column my-1.5">
			<div
				className="px-3 py-2 typography-ui-label rounded-md"
				style={{ background: 'rgba(227,70,113,0.1)', color: 'var(--status-error)', border: '1px solid rgba(227,70,113,0.2)' }}
			>
				{text}
			</div>
		</div>
	);
}
