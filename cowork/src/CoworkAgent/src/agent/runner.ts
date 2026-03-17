import path from 'path';
import fs from 'fs/promises';
import crypto from 'crypto';
import { query } from '@anthropic-ai/claude-agent-sdk';
import type { SDKAssistantMessage, SDKResultSuccess } from '@anthropic-ai/claude-agent-sdk';
import { auditLog } from './audit.js';
import { queryForgeContext } from '../services/forgeClient.js';
import { waitForApproval } from '../services/taskStore.js';
import { uploadOutputToS3 } from '../services/fileService.js';

export type OutputType = 'html' | 'markdown' | 'csv' | 'docx' | 'txt' | 'other';

export interface SseChunk {
  type: 'step' | 'tool_call' | 'result' | 'file_output' | 'approval_required' | 'approval_resolved' | 'error';
  text?: string;
  toolName?: string;
  outputType?: OutputType;
  fileName?: string;
  downloadUrl?: string;
  base64?: string;
  sizeBytes?: number;
  approvalId?: string;
  approvalToolName?: string;
  approvalToolInput?: unknown;
  approvalDescription?: string;
}

interface TaskParams {
  taskId:       string;
  userId:       string;
  userEmail:    string;
  prompt:       string;
  workingDir:   string;
  maxBudgetUsd: number;
  maxTurns:     number;
}

// Patterns that require user approval before execution
const DESTRUCTIVE_PATTERNS = [
  'rm ', 'rmdir', 'del ', '> /', 'sudo', 'chmod', 'mkfs',
  'dd ', 'curl ', 'wget ', '/etc/', '/usr/', '/root/', '/var/',
];

function requiresApproval(toolName: string, toolInput: unknown): boolean {
  if (toolName !== 'Bash') return false;
  const cmd = ((toolInput as any)?.command ?? '').toLowerCase() as string;
  return DESTRUCTIVE_PATTERNS.some(p => cmd.includes(p));
}

function describeApproval(toolName: string, toolInput: unknown): string {
  if (toolName === 'Bash') return `Run shell command: ${(toolInput as any)?.command ?? ''}`;
  return `${toolName}: ${JSON.stringify(toolInput)}`;
}

function detectOutputType(filename: string): OutputType {
  const ext = path.extname(filename).toLowerCase();
  const map: Record<string, OutputType> = {
    '.html': 'html',
    '.htm':  'html',
    '.md':   'markdown',
    '.csv':  'csv',
    '.docx': 'docx',
    '.txt':  'txt',
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

export async function* runTask(params: TaskParams): AsyncGenerator<SseChunk> {
  await auditLog({ event: 'task_started', ...params });

  let forgeContext = '';
  try {
    forgeContext = await queryForgeContext(params.prompt, params.userId, params.userEmail);
  } catch {
    // Non-fatal — task runs without FORGE context if fetch fails
  }

  const systemPrompt = forgeContext
    ? `${SYSTEM_PROMPT}\n\n## Relevant Knowledge from FORGE\n${forgeContext}`
    : SYSTEM_PROMPT;

  // Closure to emit chunks from within the preToolCall hook
  const pendingChunks: SseChunk[] = [];
  let emitChunk: ((chunk: SseChunk) => void) = (chunk) => pendingChunks.push(chunk);

  try {
    for await (const message of query({
      prompt: params.prompt,
      options: {
        cwd: params.workingDir,
        allowedTools: ['Read', 'Write', 'Edit', 'Bash'],
        maxBudgetUsd: params.maxBudgetUsd,
        maxTurns:     params.maxTurns,
        systemPrompt,
        env: {
          COWORK_TASK_ID:    params.taskId,
          COWORK_USER_ID:    params.userId,
          COWORK_USER_EMAIL: params.userEmail,
        },
        hooks: {
          preToolCall: async (toolName: string, toolInput: unknown) => {
            await auditLog({
              event: 'tool_call',
              taskId:  params.taskId,
              userId:  params.userId,
              data:    { tool: toolName, input: safeSerialize(toolInput) },
            });

            if (requiresApproval(toolName, toolInput)) {
              const approvalId   = crypto.randomUUID();
              const description  = describeApproval(toolName, toolInput);

              await auditLog({
                event: 'approval_requested',
                taskId: params.taskId,
                userId: params.userId,
                data:   { approvalId, tool: toolName, description },
              });

              emitChunk({
                type: 'approval_required',
                approvalId,
                approvalToolName:  toolName,
                approvalToolInput: toolInput,
                approvalDescription: description,
              });

              const decision = await waitForApproval(approvalId);

              await auditLog({
                event: decision === 'approve' ? 'approval_granted' : 'approval_denied',
                taskId: params.taskId,
                userId: params.userId,
                data:   { approvalId, decision },
              });

              emitChunk({
                type:       'approval_resolved',
                approvalId,
                text:       decision === 'approve' ? 'Approved — proceeding' : 'Denied — skipping',
              });

              return { action: decision === 'approve' ? 'allow' : 'block' } as const;
            }

            return { action: 'allow' } as const;
          },
        },
      },
    })) {
      // Emit any chunks buffered during preToolCall
      while (pendingChunks.length > 0) yield pendingChunks.shift()!;

      if (message.type === 'result') {
        const resultMsg = message as SDKResultSuccess;

        const outputs = await collectOutputFiles(params.workingDir, params.taskId);
        for (const chunk of outputs) yield chunk;

        await auditLog({ event: 'task_completed', taskId: params.taskId, userId: params.userId });
        yield { type: 'result', text: resultMsg.result };
      } else if (message.type === 'assistant') {
        const assistantMsg = message as SDKAssistantMessage;
        for (const block of assistantMsg.message.content ?? []) {
          if (block.type === 'text' && block.text?.trim()) {
            yield { type: 'step', text: block.text };
          } else if (block.type === 'tool_use') {
            yield { type: 'tool_call', toolName: block.name, text: describeToolCall(block) };
          }
        }
      }
    }

    // Drain any remaining buffered chunks
    while (pendingChunks.length > 0) yield pendingChunks.shift()!;

  } catch (error: any) {
    await auditLog({
      event: 'task_failed',
      taskId: params.taskId,
      userId: params.userId,
      data:   { error: error.message },
    });
    yield { type: 'error', text: error.message ?? 'Task failed' };
  }
}

async function collectOutputFiles(workingDir: string, taskId: string): Promise<SseChunk[]> {
  const chunks: SseChunk[] = [];
  try {
    const entries = await fs.readdir(workingDir, { withFileTypes: true });

    for (const entry of entries) {
      if (!entry.isFile()) continue;

      const filePath  = path.join(workingDir, entry.name);
      const stat      = await fs.stat(filePath);
      const type      = detectOutputType(entry.name);

      // Upload to S3, get pre-signed download URL
      const downloadUrl = await uploadOutputToS3(filePath, taskId, entry.name);

      // Include base64 content for inline-renderable types (max 512 KB)
      let base64: string | undefined;
      if (['html', 'markdown', 'csv'].includes(type) && stat.size < 512 * 1024) {
        const content = await fs.readFile(filePath, 'utf-8');
        base64 = Buffer.from(content).toString('base64');
      }

      chunks.push({
        type:        'file_output',
        outputType:  type,
        fileName:    entry.name,
        downloadUrl,
        base64,
        sizeBytes:   stat.size,
      });
    }
  } catch { /* Non-fatal */ }
  return chunks;
}

function describeToolCall(block: { name: string; input?: Record<string, unknown> }): string {
  if (block.name === 'Read')  return `Reading ${block.input?.['file_path'] ?? 'file'}`;
  if (block.name === 'Write') return `Writing ${block.input?.['file_path'] ?? 'file'}`;
  if (block.name === 'Edit')  return `Editing ${block.input?.['file_path'] ?? 'file'}`;
  if (block.name === 'Bash')  return `Running: ${String(block.input?.['command'] ?? '').slice(0, 80)}`;
  return `Using ${block.name}`;
}

function safeSerialize(input: unknown): unknown {
  try { return JSON.parse(JSON.stringify(input)); }
  catch { return String(input); }
}
