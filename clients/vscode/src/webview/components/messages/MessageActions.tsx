import { useState, type ReactNode } from 'react';
import { Icon } from '../icon/Icon';
import { cn } from '../../lib/utils';

interface Props {
	text: string;
}

export function MessageActions({ text }: Props) {
	const [copied, setCopied] = useState(false);

	function copy() {
		navigator.clipboard.writeText(text).then(() => {
			setCopied(true);
			setTimeout(() => setCopied(false), 1500);
		}).catch(() => undefined);
	}

	return (
		<div className="flex items-center gap-0.5 mt-1 -ml-1">
			<ActionButton title={copied ? 'Copied' : 'Copy'} onClick={copy}>
				<Icon name={copied ? 'check' : 'code'} className="w-3.5 h-3.5" />
			</ActionButton>
		</div>
	);
}

function ActionButton({ title, onClick, children }: { title: string; onClick: () => void; children: ReactNode }) {
	return (
		<button
			type="button"
			onClick={onClick}
			title={title}
			aria-label={title}
			className={cn(
				'flex items-center justify-center w-7 h-7 rounded-md transition-colors',
				'text-[var(--muted-foreground)] hover:text-[var(--foreground)] hover:bg-[var(--interactive-hover)]',
			)}
		>
			{children}
		</button>
	);
}
