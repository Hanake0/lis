export interface JsonTreeNode {
	key: string;
	value: unknown;
	type: string;
	depth: number;
	expanded: boolean;
	children?: JsonTreeNode[];
}

export function buildJsonTree(_data: unknown): JsonTreeNode[] { return []; }
export function flattenJsonTree(_nodes: JsonTreeNode[]): JsonTreeNode[] { return []; }
export function flattenTree(..._args: any[]): any { return null; }
export function getAllExpandableIds(..._args: any[]): any { return null; }
export function getExpandableIdsAboveDepth(..._args: any[]): any { return null; }
export function parseJsonToTree(..._args: any[]): any { return null; }
