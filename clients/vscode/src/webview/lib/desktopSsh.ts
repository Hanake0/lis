/**
 * OC desktop's SSH-tunnel manager — `ssh -L` port forwards to a remote OC
 * instance, used by the "Remote Instances" settings page. Lis doesn't have
 * an SSH manager (the user already has VS Code's Remote-SSH extension for
 * that). Type definitions are kept verbatim so OC components compile; the
 * functions are documented noops that return empty data.
 */

export type DesktopSshPortForwardType = 'local' | 'remote' | 'dynamic';

export interface DesktopSshPortForward {
	type:        DesktopSshPortForwardType;
	localPort:   number;
	remotePort?: number;
	remoteHost?: string;
}

export interface DesktopSshInstance {
	id:           string;
	name:         string;
	host:         string;
	user?:        string;
	port?:        number;
	phase?:       string;
	forwards?:    DesktopSshPortForward[];
	connected?:   boolean;
	error?:       string;
}

const EMPTY_LOGS: string[] = [];

export function getDesktopSsh(): null { return null; }

/** Read the rolling log for an SSH instance. Empty array in Lis. */
export function desktopSshLogs(_instanceId: string, _limit?: number): string[] { return EMPTY_LOGS; }

/** Clear the rolling log. Noop in Lis. */
export function desktopSshLogsClear(_instanceId: string): void { /* noop */ }
