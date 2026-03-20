export interface DesignTool {
    name: string;
    description: string;
    inputSchema: object;
}
export declare const saveScreenTool: DesignTool;
export declare const listScreensTool: DesignTool;
export declare const DESIGN_TOOLS: DesignTool[];
