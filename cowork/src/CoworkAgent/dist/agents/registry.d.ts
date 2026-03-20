export interface AgentDefinition {
    id: string;
    name: string;
    description: string;
    icon: string;
    color: string;
    systemPromptPath: string;
    kbConfig: {
        kbId: string;
        dataSourceIds: string[];
        fallbackToCorpKb: boolean;
    };
    allowedMcpServers: string[];
    approvalOverrides: {
        require: string[];
        skip: string[];
    };
    workspaceComponent: string;
}
export declare const AGENT_REGISTRY: Record<string, AgentDefinition>;
