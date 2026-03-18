"use strict";
var __createBinding = (this && this.__createBinding) || (Object.create ? (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    var desc = Object.getOwnPropertyDescriptor(m, k);
    if (!desc || ("get" in desc ? !m.__esModule : desc.writable || desc.configurable)) {
      desc = { enumerable: true, get: function() { return m[k]; } };
    }
    Object.defineProperty(o, k2, desc);
}) : (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    o[k2] = m[k];
}));
var __setModuleDefault = (this && this.__setModuleDefault) || (Object.create ? (function(o, v) {
    Object.defineProperty(o, "default", { enumerable: true, value: v });
}) : function(o, v) {
    o["default"] = v;
});
var __importStar = (this && this.__importStar) || (function () {
    var ownKeys = function(o) {
        ownKeys = Object.getOwnPropertyNames || function (o) {
            var ar = [];
            for (var k in o) if (Object.prototype.hasOwnProperty.call(o, k)) ar[ar.length] = k;
            return ar;
        };
        return ownKeys(o);
    };
    return function (mod) {
        if (mod && mod.__esModule) return mod;
        var result = {};
        if (mod != null) for (var k = ownKeys(mod), i = 0; i < k.length; i++) if (k[i] !== "default") __createBinding(result, mod, k[i]);
        __setModuleDefault(result, mod);
        return result;
    };
})();
var __importDefault = (this && this.__importDefault) || function (mod) {
    return (mod && mod.__esModule) ? mod : { "default": mod };
};
Object.defineProperty(exports, "__esModule", { value: true });
const express_1 = __importDefault(require("express"));
const path = __importStar(require("path"));
const dotenv_1 = __importDefault(require("dotenv"));
dotenv_1.default.config();
const index_js_1 = require("@modelcontextprotocol/sdk/server/index.js");
const streamableHttp_js_1 = require("@modelcontextprotocol/sdk/server/streamableHttp.js");
const types_js_1 = require("@modelcontextprotocol/sdk/types.js");
const db_1 = require("./db");
const auth_1 = require("./auth");
const search_1 = require("./tools/search");
const add_1 = require("./tools/add");
const list_1 = require("./tools/list");
const delete_1 = require("./tools/delete");
const app = (0, express_1.default)();
app.use(express_1.default.json());
app.get('/cli/memory.py', (_req, res) => {
    res.setHeader('Content-Type', 'text/plain');
    res.sendFile(path.join(__dirname, '../cli/memory.py'));
});
app.get('/health', (_req, res) => res.json({ status: 'ok' }));
app.all('/mcp', async (req, res) => {
    const user = await (0, auth_1.authenticate)(req);
    if (!user) {
        res.status(401).json({ error: 'Unauthorized' });
        return;
    }
    const server = new index_js_1.Server({ name: 'fip-memory', version: '1.0.0' }, { capabilities: { tools: {} } });
    server.setRequestHandler(types_js_1.ListToolsRequestSchema, async () => ({
        tools: [
            {
                name: 'memory_search',
                description: 'Search org-level and personal memory for relevant context. Call at session start for background on a topic.',
                inputSchema: {
                    type: 'object',
                    properties: {
                        query: { type: 'string', description: 'What to search for' },
                        project: { type: 'string', description: 'Filter to a specific project (e.g. iaapa, firm). Omit for global.' },
                        limit: { type: 'number', description: 'Max results (default 10, max 20)' },
                    },
                    required: ['query'],
                },
            },
            {
                name: 'memory_add',
                description: 'Store a decision, lesson learned, or context for future sessions. Use at session end for anything worth remembering.',
                inputSchema: {
                    type: 'object',
                    properties: {
                        content: { type: 'string', description: 'The memory to store (1-3 sentences)' },
                        entry_type: { type: 'string', enum: ['decision', 'lesson', 'context', 'note'] },
                        project: { type: 'string', description: 'Project tag (e.g. iaapa, firm)' },
                        scope: { type: 'string', enum: ['personal', 'org'], description: 'personal = only you; org = shared. Default: personal.' },
                        confirmed: { type: 'boolean', description: 'Set true to confirm org write after confirmation_required response' },
                    },
                    required: ['content'],
                },
            },
            {
                name: 'memory_list',
                description: 'List recent memory entries for a project or scope.',
                inputSchema: {
                    type: 'object',
                    properties: {
                        project: { type: 'string' },
                        scope: { type: 'string', enum: ['personal', 'org', 'all'] },
                        limit: { type: 'number', description: 'Default 20, max 50' },
                    },
                },
            },
            {
                name: 'memory_delete',
                description: 'Delete a personal memory entry by ID. Admins can delete any entry.',
                inputSchema: {
                    type: 'object',
                    properties: {
                        id: { type: 'string', description: 'Entry UUID to delete' },
                    },
                    required: ['id'],
                },
            },
        ],
    }));
    server.setRequestHandler(types_js_1.CallToolRequestSchema, async (request) => {
        const { name, arguments: args } = request.params;
        let result;
        switch (name) {
            case 'memory_search':
                result = await (0, search_1.memorySearch)(args, user);
                break;
            case 'memory_add':
                result = await (0, add_1.memoryAdd)(args, user);
                break;
            case 'memory_list':
                result = await (0, list_1.memoryList)(args, user);
                break;
            case 'memory_delete':
                result = await (0, delete_1.memoryDelete)(args, user);
                break;
            default:
                throw new Error(`Unknown tool: ${name}`);
        }
        return {
            content: [{ type: 'text', text: JSON.stringify(result, null, 2) }],
        };
    });
    const transport = new streamableHttp_js_1.StreamableHTTPServerTransport({ sessionIdGenerator: undefined });
    await server.connect(transport);
    await transport.handleRequest(req, res, req.body);
    await server.close();
});
const PORT = parseInt(process.env.PORT || '3100');
async function main() {
    await (0, db_1.initDb)();
    app.listen(PORT, () => {
        console.log(`[mcp-memory] listening on port ${PORT}`);
    });
}
main().catch(console.error);
