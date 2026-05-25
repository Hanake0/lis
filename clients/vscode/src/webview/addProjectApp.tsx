import * as React from 'react';
import { DirectoryExplorerDialog } from './components/session/DirectoryExplorerDialog';
import { TooltipProvider } from './components/ui/tooltip';
import { ErrorBoundary } from './components/ui/ErrorBoundary';
import { Toaster } from './components/ui/sonner';
import { I18nProvider } from './lib/i18n';
import { vscode } from './vscodeApi';

/**
 * Standalone Add-Project picker. Mounted into its own VS Code WebviewPanel
 * (singleton, opened by the host's `lis.openAddProject` command). The dialog
 * is forced open via `open={true}` — `onOpenChange(false)` fires when the
 * user picks a directory, hits Escape, or clicks the backdrop, at which
 * point we post `close_add_project` so the host disposes the editor tab.
 *
 * The dialog detects the `__LIS_ADD_PROJECT_MODE` flag (set in addProject.ts
 * before mount) and renders via MobileOverlayPanel — that's OC's centered-
 * card overlay surface, exactly the modal-feel layout we want here.
 */
export function AddProjectApp() {
    React.useEffect(() => {
        document.documentElement.classList.add('dark');
    }, []);

    const handleOpenChange = React.useCallback((next: boolean) => {
        if (next) return;
        vscode.postMessage({ type: 'close_add_project' });
    }, []);

    return (
        <I18nProvider>
            <ErrorBoundary>
                <TooltipProvider delayDuration={300} skipDelayDuration={150}>
                    <div
                        className="h-full w-full text-foreground dark"
                        style={{
                            background: 'rgba(0,0,0,0.55)',
                            backdropFilter: 'blur(2px)',
                        }}
                    >
                        <DirectoryExplorerDialog open={true} onOpenChange={handleOpenChange} />
                        <Toaster />
                    </div>
                </TooltipProvider>
            </ErrorBoundary>
        </I18nProvider>
    );
}
