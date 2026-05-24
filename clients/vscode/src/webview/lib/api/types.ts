export interface RuntimeAPIs {
  vscode?: {
    executeCommand: (command: string, ...args: unknown[]) => Promise<void>;
  };
  github?: unknown;
}
