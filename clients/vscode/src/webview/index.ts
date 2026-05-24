import { h, render } from 'preact';
import { App } from './app';
import { vscode } from './vscodeApi';

render(h(App, null), document.getElementById('app')!);
vscode.postMessage({ type: 'ready' });
