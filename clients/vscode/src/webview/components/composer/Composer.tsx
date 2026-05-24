import { useState } from 'preact/hooks';
import { chatId, composerModel, composerEffort, composerProfile, composerThinking, currentAgent, contextOpen } from '../../state';
import { vscode } from '../../vscodeApi';
import { AgentPicker } from './AgentPicker';
import { ModelInput } from './ModelInput';
import { EffortPicker } from './EffortPicker';
import { ThinkingModePicker } from './ThinkingModePicker';
import { ProfilePicker } from './ProfilePicker';
import { MessageInput } from './MessageInput';
import { SendButton } from './SendButton';
import { SaveButton } from './SaveButton';

export function Composer() {
	const [text, setText] = useState('');

	function send() {
		const trimmed = text.trim();
		if (!trimmed || !chatId.value) return;
		vscode.postMessage({
			type:          'send',
			text:          trimmed,
			model:         composerModel.value   || undefined,
			effort:        composerEffort.value  || undefined,
			tool_profile:  composerProfile.value || undefined,
			thinking_mode: composerThinking.value || undefined,
		});
		setText('');
	}

	function onSave() {
		const id = chatId.value;
		if (!id) return;
		vscode.postMessage({
			type: 'send_frame',
			frame: {
				type:         'save_overrides',
				chat_id:      id,
				model:        composerModel.value   || undefined,
				effort:       composerEffort.value  || undefined,
				tool_profile: composerProfile.value || undefined,
			},
		});
	}

	const agent   = currentAgent.value;
	const isDirty =
		composerModel.value   !== (agent?.model        ?? '') ||
		composerEffort.value  !== (agent?.effort       ?? '') ||
		composerProfile.value !== (agent?.tool_profile ?? '');

	return (
		<div class="composer">
			<div class="composer-controls">
				<AgentPicker />
				<ModelInput />
				<EffortPicker />
				<ThinkingModePicker />
				<ProfilePicker />
				<button
					type="button"
					class={`chip chip--icon${contextOpen.value ? ' chip--open' : ''}`}
					title="Context window"
					onClick={() => { contextOpen.value = !contextOpen.value; }}
				>
					<i class="codicon codicon-pulse" />
				</button>
			</div>

			<div class="composer-input-row">
				<MessageInput value={text} onChange={setText} onSend={send} />
				<div class="composer-actions">
					<SendButton disabled={!text.trim()} onClick={send} />
					<SaveButton dirty={isDirty} onClick={onSave} />
				</div>
			</div>
		</div>
	);
}
