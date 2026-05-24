import { createElement } from 'react';
import { createRoot } from 'react-dom/client';
import { App } from './app';
import { vscode } from './vscodeApi';

const isSidebarMode = (window as any).__LIS_SIDEBAR_MODE === true;

if (isSidebarMode) {
	import('./sidebarOC').then(({ SidebarOCApp, installSidebarFrameHandler }) => {
		installSidebarFrameHandler();
		createRoot(document.getElementById('app')!).render(createElement(SidebarOCApp, null));
		vscode.postMessage({ type: 'ready' });
	});
} else {
	createRoot(document.getElementById('app')!).render(createElement(App, null));
	vscode.postMessage({ type: 'ready' });
}
