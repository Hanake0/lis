import { connection } from '../state';

export function StatusBar() {
	const state = connection.value;
	const label = state === 'connected' ? 'connected' : state === 'connecting' ? 'connecting…' : 'disconnected';
	return <div class={`status-bar status-bar--${state}`}>{label}</div>;
}
