// Design Agent — Tool Stubs
// MCP tool definitions for future design-specific tooling.
// Phase 1: unused — design agent uses generic Claude file tools (Read/Write/Edit).
// Phase 2: add save_screen, export_to_figma, upload_reference_image, etc.

export interface DesignTool {
  name:        string;
  description: string;
  inputSchema: object;
}

// Stub: save_screen — explicitly save generated HTML to working dir with metadata
export const saveScreenTool: DesignTool = {
  name:        'save_screen',
  description: 'Save a generated HTML screen to the working directory with metadata.',
  inputSchema: {
    type: 'object',
    properties: {
      filename:    { type: 'string', description: 'Output filename (e.g. screen.html)' },
      htmlContent: { type: 'string', description: 'Complete HTML/CSS content' },
      summary:     { type: 'string', description: 'One-sentence description of what was generated' },
    },
    required: ['filename', 'htmlContent'],
  },
};

// Stub: list_screens — retrieve all screens for the current project
export const listScreensTool: DesignTool = {
  name:        'list_screens',
  description: 'List all generated screens for the current project.',
  inputSchema: {
    type: 'object',
    properties: {
      projectId: { type: 'string', description: 'Project ID to list screens for' },
    },
    required: ['projectId'],
  },
};

// All design tools (exported for future MCP server registration)
export const DESIGN_TOOLS: DesignTool[] = [
  saveScreenTool,
  listScreensTool,
];
