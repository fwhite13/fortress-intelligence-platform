"use strict";
// AGENT REGISTRY
// Maps agentId → agent definition.
// Used by /agents/* routes to look up configuration per agent.
Object.defineProperty(exports, "__esModule", { value: true });
exports.AGENT_REGISTRY = void 0;
exports.AGENT_REGISTRY = {
    marketing: {
        id: 'marketing',
        name: 'Marketing Agent',
        description: 'Campaign copy, email sequences, audience targeting, brand voice.',
        icon: 'Campaign',
        color: '#d4af37',
        systemPromptPath: 'agents/marketing/system-prompt.md',
        kbConfig: {
            kbId: process.env.COWORK_MARKETING_KB_ID ?? '',
            dataSourceIds: (process.env.COWORK_MARKETING_DS_IDS ?? '').split(',').filter(Boolean),
            fallbackToCorpKb: true,
        },
        allowedMcpServers: ['hubspot', 'klaviyo', 'ahrefs'],
        approvalOverrides: { require: [], skip: [] },
        workspaceComponent: 'MarketingWorkspace',
    },
    analyst: {
        id: 'analyst',
        name: 'Financial Analyst',
        description: 'Investment memos, earnings analysis, financial models.',
        icon: 'BarChart',
        color: '#0369a1',
        systemPromptPath: 'agents/analyst/system-prompt.md',
        kbConfig: {
            kbId: process.env.COWORK_ANALYST_KB_ID ?? '',
            dataSourceIds: (process.env.COWORK_ANALYST_DS_IDS ?? '').split(',').filter(Boolean),
            fallbackToCorpKb: false,
        },
        allowedMcpServers: ['brave-search'],
        approvalOverrides: { require: [], skip: [] },
        workspaceComponent: 'AnalystWorkspace',
    },
    techwriter: {
        id: 'techwriter',
        name: 'Tech Writer',
        description: 'Documentation, user guides, API references, changelogs.',
        icon: 'Article',
        color: '#0891b2',
        systemPromptPath: 'agents/techwriter/system-prompt.md',
        kbConfig: {
            kbId: process.env.COWORK_TECHWRITER_KB_ID ?? '',
            dataSourceIds: (process.env.COWORK_TECHWRITER_DS_IDS ?? '').split(',').filter(Boolean),
            fallbackToCorpKb: true,
        },
        allowedMcpServers: [],
        approvalOverrides: { require: [], skip: [] },
        workspaceComponent: 'TechWriterWorkspace',
    },
    ops: {
        id: 'ops',
        name: 'Operations Agent',
        description: 'SOPs, process documentation, workflow optimization.',
        icon: 'Settings',
        color: '#6b7280',
        systemPromptPath: 'agents/ops/system-prompt.md',
        kbConfig: {
            kbId: process.env.COWORK_OPS_KB_ID ?? '',
            dataSourceIds: (process.env.COWORK_OPS_DS_IDS ?? '').split(',').filter(Boolean),
            fallbackToCorpKb: true,
        },
        allowedMcpServers: ['slack'],
        approvalOverrides: { require: [], skip: [] },
        workspaceComponent: 'OpsWorkspace',
    },
    design: {
        id: 'design',
        name: 'Design Agent',
        description: 'Generate responsive HTML/CSS UI screens from text descriptions. Iterate, create variants, export to HTML or Blazor components.',
        icon: 'Palette',
        color: '#7C3AED',
        systemPromptPath: 'agents/design/system-prompt.md',
        kbConfig: {
            kbId: process.env.COWORK_DESIGN_KB_ID ?? '',
            dataSourceIds: (process.env.COWORK_DESIGN_DS_IDS ?? '').split(',').filter(Boolean),
            fallbackToCorpKb: false,
        },
        allowedMcpServers: [],
        approvalOverrides: { require: [], skip: [] },
        workspaceComponent: 'DesignWorkspace',
    },
};
//# sourceMappingURL=registry.js.map