"use strict";
var __importDefault = (this && this.__importDefault) || function (mod) {
    return (mod && mod.__esModule) ? mod : { "default": mod };
};
Object.defineProperty(exports, "__esModule", { value: true });
exports.runTask = runTask;
const path_1 = __importDefault(require("path"));
const promises_1 = __importDefault(require("fs/promises"));
const crypto_1 = __importDefault(require("crypto"));
const claude_agent_sdk_1 = require("@anthropic-ai/claude-agent-sdk");
const audit_js_1 = require("./audit.js");
const forgeClient_js_1 = require("../services/forgeClient.js");
const taskStore_js_1 = require("../services/taskStore.js");
const fileService_js_1 = require("../services/fileService.js");
// Patterns that require user approval before execution
const DESTRUCTIVE_PATTERNS = [
    'rm ', 'rmdir', 'del ', '> /', 'sudo', 'chmod', 'mkfs',
    'dd ', 'curl ', 'wget ', '/etc/', '/usr/', '/root/', '/var/',
];
function requiresApproval(toolName, toolInput) {
    if (toolName !== 'Bash')
        return false;
    const cmd = (toolInput?.command ?? '').toLowerCase();
    return DESTRUCTIVE_PATTERNS.some(p => cmd.includes(p));
}
function describeApproval(toolName, toolInput) {
    if (toolName === 'Bash')
        return `Run shell command: ${toolInput?.command ?? ''}`;
    return `${toolName}: ${JSON.stringify(toolInput)}`;
}
function detectOutputType(filename) {
    const ext = path_1.default.extname(filename).toLowerCase();
    const map = {
        '.html': 'html',
        '.htm': 'html',
        '.md': 'markdown',
        '.csv': 'csv',
        '.docx': 'docx',
        '.txt': 'txt',
    };
    return map[ext] ?? 'other';
}
const SYSTEM_PROMPT = `You are FAIT Cowork — an AI assistant at Fortress Asset Management.
You complete business tasks for non-technical users: creating HTML prototypes, drafting documents,
summarizing files, and analyzing data.

Your working directory contains the user's uploaded files. You create output files there.
Explain each step as you work — users see your progress in real time.

Output guidelines by task type:
- Documents / reports: write a .md file (Markdown). Use headers, bullet points, tables.
- Data analysis: write a .md file for insights + optionally a .csv file for tabular data.
- HTML prototypes: write a .html file (self-contained, inline CSS, no CDN links).
- General text: write a .txt file if no other format is better.
- If creating multiple output files, name them clearly (e.g. report.md, data.csv).

When creating HTML, use inline CSS only (no external CDN links — the output must be self-contained).
When finished, explicitly state the name(s) of the output file(s) you created.

Data sovereignty: You run on Fortress AM's private AWS infrastructure. No data leaves Fortress AM.`;
async function* runTask(params) {
    await (0, audit_js_1.auditLog)({ event: 'task_started', ...params });
    // Fetch persistent instructions before FORGE query
    let persistentInstructions = '';
    try {
        const redis = await (0, taskStore_js_1.getRedis)();
        const instrData = await redis.hGetAll(`cowork:user:${params.userId}:instructions`);
        persistentInstructions = instrData?.text ?? '';
    }
    catch {
        // Non-fatal — proceed without instructions
    }
    if (persistentInstructions) {
        await (0, audit_js_1.auditLog)({
            event: 'instructions_loaded',
            taskId: params.taskId,
            userId: params.userId,
            data: { length: persistentInstructions.length }, // NO content field — must not log instruction text
        });
    }
    let forgeContext = '';
    try {
        forgeContext = await (0, forgeClient_js_1.queryForgeContextCached)(params.prompt, params.userId, params.userEmail);
    }
    catch {
        // Non-fatal — task runs without FORGE context if fetch fails
    }
    const effectiveSystemPrompt = params.systemPromptOverride?.trim()
        ? params.systemPromptOverride
        : SYSTEM_PROMPT;
    const systemPrompt = [
        effectiveSystemPrompt,
        persistentInstructions
            ? `## Your Standing Instructions\n${persistentInstructions}`
            : '',
        forgeContext
            ? `## Relevant Knowledge from FORGE\n${forgeContext}`
            : '',
    ].filter(Boolean).join('\n\n');
    // Build SearchForge MCP server per-task (closure captures userId and userEmail)
    const forgeMcpServer = (0, forgeClient_js_1.buildSearchForgeMcpServer)(params.userId, params.userEmail);
    // Closure to emit chunks from within the preToolCall hook
    const pendingChunks = [];
    let emitChunk = (chunk) => pendingChunks.push(chunk);
    try {
        for await (const message of (0, claude_agent_sdk_1.query)({
            prompt: params.prompt,
            options: {
                cwd: params.workingDir,
                allowedTools: ['Read', 'Write', 'Edit', 'Bash', 'mcp__forge__SearchForge'],
                mcpServers: { forge: forgeMcpServer },
                maxBudgetUsd: params.maxBudgetUsd,
                maxTurns: params.maxTurns,
                systemPrompt,
                env: {
                    COWORK_TASK_ID: params.taskId,
                    COWORK_USER_ID: params.userId,
                    COWORK_USER_EMAIL: params.userEmail,
                },
                hooks: {
                    PreToolUse: [{
                            hooks: [async (hookInput) => {
                                    const preHook = hookInput;
                                    const toolName = preHook.tool_name;
                                    const toolInput = preHook.tool_input;
                                    await (0, audit_js_1.auditLog)({
                                        event: 'tool_call',
                                        taskId: params.taskId,
                                        userId: params.userId,
                                        data: { tool: toolName, input: safeSerialize(toolInput) },
                                    });
                                    if (requiresApproval(toolName, toolInput)) {
                                        const approvalId = crypto_1.default.randomUUID();
                                        const description = describeApproval(toolName, toolInput);
                                        await (0, audit_js_1.auditLog)({
                                            event: 'approval_requested',
                                            taskId: params.taskId,
                                            userId: params.userId,
                                            data: { approvalId, tool: toolName, description },
                                        });
                                        emitChunk({
                                            type: 'approval_required',
                                            approvalId,
                                            approvalToolName: toolName,
                                            approvalToolInput: toolInput,
                                            approvalDescription: description,
                                        });
                                        const decision = await (0, taskStore_js_1.waitForApproval)(approvalId);
                                        await (0, audit_js_1.auditLog)({
                                            event: decision === 'approve' ? 'approval_granted' : 'approval_denied',
                                            taskId: params.taskId,
                                            userId: params.userId,
                                            data: { approvalId, decision },
                                        });
                                        emitChunk({
                                            type: 'approval_resolved',
                                            approvalId,
                                            text: decision === 'approve' ? 'Approved — proceeding' : 'Denied — skipping',
                                        });
                                        return {
                                            decision: decision === 'approve' ? 'approve' : 'block',
                                        };
                                    }
                                    return { decision: 'approve' };
                                }],
                        }],
                },
            },
        })) {
            // Emit any chunks buffered during preToolCall
            while (pendingChunks.length > 0)
                yield pendingChunks.shift();
            if (message.type === 'result') {
                const resultMsg = message;
                const outputs = await collectOutputFiles(params.workingDir, params.taskId);
                for (const chunk of outputs)
                    yield chunk;
                await (0, audit_js_1.auditLog)({ event: 'task_completed', taskId: params.taskId, userId: params.userId });
                yield { type: 'result', text: resultMsg.result };
            }
            else if (message.type === 'assistant') {
                const assistantMsg = message;
                for (const block of assistantMsg.message.content ?? []) {
                    if (block.type === 'text' && block.text?.trim()) {
                        yield { type: 'step', text: block.text };
                    }
                    else if (block.type === 'tool_use') {
                        yield { type: 'tool_call', toolName: block.name, text: describeToolCall(block) };
                    }
                }
            }
            // Check cancellation AFTER processing each message (not inside hooks)
            const redis = await (0, taskStore_js_1.getRedis)();
            const cancelled = await redis.get(`cowork:cancel:${params.taskId}`);
            if (cancelled) {
                await (0, audit_js_1.auditLog)({ event: 'task_cancelled', taskId: params.taskId, userId: params.userId });
                yield { type: 'error', text: 'Task cancelled' };
                return;
            }
        }
        // Drain any remaining buffered chunks
        while (pendingChunks.length > 0)
            yield pendingChunks.shift();
    }
    catch (error) {
        await (0, audit_js_1.auditLog)({
            event: 'task_failed',
            taskId: params.taskId,
            userId: params.userId,
            data: { error: error.message },
        });
        yield { type: 'error', text: error.message ?? 'Task failed' };
    }
}
async function collectOutputFiles(workingDir, taskId) {
    const chunks = [];
    try {
        const entries = await promises_1.default.readdir(workingDir, { withFileTypes: true });
        for (const entry of entries) {
            if (!entry.isFile())
                continue;
            const filePath = path_1.default.join(workingDir, entry.name);
            const stat = await promises_1.default.stat(filePath);
            const type = detectOutputType(entry.name);
            // Upload to S3, get pre-signed download URL
            const downloadUrl = await (0, fileService_js_1.uploadOutputToS3)(filePath, taskId, entry.name);
            // Include base64 content for inline-renderable types (max 512 KB)
            let base64;
            if (['html', 'markdown', 'csv'].includes(type) && stat.size < 512 * 1024) {
                const content = await promises_1.default.readFile(filePath, 'utf-8');
                base64 = Buffer.from(content).toString('base64');
            }
            chunks.push({
                type: 'file_output',
                outputType: type,
                fileName: entry.name,
                downloadUrl,
                base64,
                sizeBytes: stat.size,
            });
        }
    }
    catch { /* Non-fatal */ }
    return chunks;
}
function describeToolCall(block) {
    if (block.name === 'Read')
        return `Reading ${block.input?.['file_path'] ?? 'file'}`;
    if (block.name === 'Write')
        return `Writing ${block.input?.['file_path'] ?? 'file'}`;
    if (block.name === 'Edit')
        return `Editing ${block.input?.['file_path'] ?? 'file'}`;
    if (block.name === 'Bash')
        return `Running: ${String(block.input?.['command'] ?? '').slice(0, 80)}`;
    return `Using ${block.name}`;
}
function safeSerialize(input) {
    try {
        return JSON.parse(JSON.stringify(input));
    }
    catch {
        return String(input);
    }
}
//# sourceMappingURL=runner.js.map