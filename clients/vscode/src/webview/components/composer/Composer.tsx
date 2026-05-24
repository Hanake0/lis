import { useState } from 'react';
import { useChatStore } from '../../store';
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

	const chatId   = useChatStore(s => s.chatId);
	const model    = useChatStore(s => s.composerModel);
	const effort   = useChatStore(s => s.composerEffort);
	const profile  = useChatStore(s => s.composerProfile);
	const thinking = useChatStore(s => s.composerThinking);
	const agent    = useChatStore(s => s.currentAgent);

	function send() {
		const trimmed = text.trim();
		if (!trimmed || !chatId) return;
		vscode.postMessage({
			type:          'send',
			text:          trimmed,
			model:         model    || undefined,
			effort:        effort   || undefined,
			tool_profile:  profile  || undefined,
			thinking_mode: thinking || undefined,
		});
		setText('');
	}

	function onSave() {
		if (!chatId) return;
		vscode.postMessage({
			type: 'send_frame',
			frame: {
				type:         'save_overrides',
				chat_id:      chatId,
				model:        model   || undefined,
				effort:       effort  || undefined,
				tool_profile: profile || undefined,
			},
		});
	}

	const isDirty =
		model   !== (agent?.model        ?? '') ||
		effort  !== (agent?.effort       ?? '') ||
		profile !== (agent?.tool_profile ?? '');

	return (
		<div className="flex-shrink-0 pt-2 pb-3" style={{ background: 'var(--background)' }}>
			<div className="chat-input-column">
				<div
					className="rounded-2xl overflow-hidden"
					style={{
						background: 'var(--surface-elevated)',
						border:     '1px solid var(--border)',
						boxShadow:  '0 1px 3px rgba(0,0,0,0.08), 0 6px 20px rgba(0,0,0,0.14)',
					}}
				>
					<MessageInput value={text} onChange={setText} onSend={send} />

					<div className="flex items-center gap-1 px-2 py-1.5">
						<AgentPicker />
						<div className="flex-1 flex items-center gap-0.5 flex-wrap min-w-0">
							<ModelInput />
							<EffortPicker />
							<ThinkingModePicker />
							<ProfilePicker />
						</div>
						<SaveButton dirty={isDirty} onClick={onSave} />
						<SendButton disabled={!text.trim()} onClick={send} />
					</div>
				</div>
			</div>
		</div>
	);
}
