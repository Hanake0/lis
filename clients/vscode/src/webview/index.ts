import { createElement } from 'react';
import { createRoot } from 'react-dom/client';
import { App } from './app';
import { vscode } from './vscodeApi';

createRoot(document.getElementById('app')!).render(createElement(App, null));
vscode.postMessage({ type: 'ready' });
