// Per-message metadata storage and expandable detail panel.

interface TokenMeta {
	input_tokens?: number;
	output_tokens?: number;
	cache_read_tokens?: number;
	cache_creation_tokens?: number;
	thinking_tokens?: number;
}

const metadataStore = new Map<string, TokenMeta>();

export function storeMetadata(id: string, meta: TokenMeta): void {
	metadataStore.set(id, meta);
}

export function attachMetadataBadge(el: HTMLElement, id: string): void {
	const meta = metadataStore.get(id);
	if (!meta || (!meta.input_tokens && !meta.output_tokens)) return;

	const total = (meta.input_tokens ?? 0) + (meta.output_tokens ?? 0);
	const badge = document.createElement('span');
	badge.className = 'meta-badge';
	badge.textContent = formatTokens(total);
	badge.title = 'Click to see token details';
	badge.addEventListener('click', e => {
		e.stopPropagation();
		toggleMetaPanel(el, id);
	});
	el.appendChild(badge);
}

function toggleMetaPanel(msgEl: HTMLElement, id: string): void {
	const existing = msgEl.querySelector('.meta-panel');
	if (existing) { existing.remove(); return; }

	const meta = metadataStore.get(id);
	if (!meta) return;

	const panel = document.createElement('div');
	panel.className = 'meta-panel';

	const lines: string[] = [];
	if (meta.input_tokens)          lines.push(`Input: ${formatTokens(meta.input_tokens)}`);
	if (meta.output_tokens)         lines.push(`Output: ${formatTokens(meta.output_tokens)}`);
	if (meta.cache_read_tokens)     lines.push(`Cache read: ${formatTokens(meta.cache_read_tokens)}`);
	if (meta.cache_creation_tokens) lines.push(`Cache write: ${formatTokens(meta.cache_creation_tokens)}`);
	if (meta.thinking_tokens)       lines.push(`Thinking: ${formatTokens(meta.thinking_tokens)}`);

	panel.textContent = lines.join('\n');
	msgEl.appendChild(panel);
}

function formatTokens(n: number): string {
	return n >= 1000 ? `${(n / 1000).toFixed(1)}k` : `${n}`;
}
