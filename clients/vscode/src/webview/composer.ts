// Composer controls — agent, model, effort, tool profile selectors.

import { vscode } from './vscodeApi';
import { getChatId } from './dispatcher';

interface AgentInfo {
	name: string;
	display_name?: string;
	model: string;
	effort?: string;
	tool_profile?: string;
	is_default: boolean;
}

let agents: AgentInfo[] = [];

const composerEl = document.getElementById('composer')!;
let controlsRow: HTMLDivElement | null = null;

export function initControls(): void {
	controlsRow = document.createElement('div');
	controlsRow.className = 'composer-controls';
	controlsRow.innerHTML = `
		<select id="ctrl-agent" title="Agent"></select>
		<input  id="ctrl-model" type="text" placeholder="model" title="Model override" />
		<select id="ctrl-effort" title="Thinking effort">
			<option value="">effort</option>
			<option value="low">low</option>
			<option value="medium">medium</option>
			<option value="high">high</option>
			<option value="xhigh">xhigh</option>
			<option value="max">max</option>
		</select>
		<select id="ctrl-thinking-mode" title="Thinking mode (auto picks adaptive for Opus 4.7+, budget otherwise)">
			<option value="">auto</option>
			<option value="budget">budget</option>
			<option value="adaptive">adaptive</option>
		</select>
		<select id="ctrl-profile" title="Tool profile">
			<option value="">profile</option>
			<option value="minimal">minimal</option>
			<option value="standard">standard</option>
			<option value="coding">coding</option>
			<option value="full">full</option>
		</select>
		<button id="ctrl-save" type="button" title="Save overrides to agent">Save</button>
	`;
	composerEl.insertBefore(controlsRow, composerEl.firstChild);

	getEl<HTMLSelectElement>('ctrl-agent').addEventListener('change', onAgentChange);
	getEl<HTMLButtonElement>('ctrl-save').addEventListener('click', onSave);
}

export function getOverrides(): { model?: string; effort?: string; tool_profile?: string; thinking_mode?: string } {
	const model        = getEl<HTMLInputElement>('ctrl-model').value.trim();
	const effort       = getEl<HTMLSelectElement>('ctrl-effort').value;
	const profile      = getEl<HTMLSelectElement>('ctrl-profile').value;
	const thinkingMode = getEl<HTMLSelectElement>('ctrl-thinking-mode').value;
	return {
		model:         model || undefined,
		effort:        effort || undefined,
		tool_profile:  profile || undefined,
		thinking_mode: thinkingMode || undefined,
	};
}

export function handleAgentInfo(agentName: string, model: string, effort?: string, toolProfile?: string): void {
	getEl<HTMLInputElement>('ctrl-model').value     = model;
	getEl<HTMLSelectElement>('ctrl-effort').value   = effort ?? '';
	getEl<HTMLSelectElement>('ctrl-profile').value  = toolProfile ?? '';

	const agentSelect = getEl<HTMLSelectElement>('ctrl-agent');
	if (agentSelect.value !== agentName) {
		for (let i = 0; i < agentSelect.options.length; i++) {
			if (agentSelect.options[i].value === agentName) { agentSelect.value = agentName; break; }
		}
	}
}

export function handleAgentList(list: AgentInfo[]): void {
	agents = list;
	const select = getEl<HTMLSelectElement>('ctrl-agent');
	select.innerHTML = '';
	for (const a of agents) {
		const opt = document.createElement('option');
		opt.value = a.name;
		opt.textContent = a.display_name ?? a.name;
		if (a.is_default) opt.textContent += ' ●';
		select.appendChild(opt);
	}
}

function onAgentChange(): void {
	const chatId = getChatId();
	if (!chatId) return;
	const name = getEl<HTMLSelectElement>('ctrl-agent').value;
	vscode.postMessage({ type: 'send_frame', frame: { type: 'set_agent', chat_id: chatId, agent_name: name } });
}

function onSave(): void {
	const chatId = getChatId();
	if (!chatId) return;
	const overrides = getOverrides();
	vscode.postMessage({ type: 'send_frame', frame: { type: 'save_overrides', chat_id: chatId, ...overrides } });
}

function getEl<T extends HTMLElement>(id: string): T {
	return document.getElementById(id) as T;
}
