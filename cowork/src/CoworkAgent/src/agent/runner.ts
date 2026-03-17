import path from 'path';
import fs from 'fs/promises';
import { query } from '@anthropic-ai/claude-agent-sdk';
import type { SDKAssistantMessage, SDKResultSuccess } from '@anthropic-ai/claude-agent-sdk';
import { auditLog } from './audit.js';
import { queryForgeContext } from '../services/forgeClient.js';

interface TaskParams {
  taskId:      string;
  userId:      string;
  userEmail:   string;
  prompt:      string;
  workingDir:  string;
  maxBudgetUsd: number;
  maxTurns:    number;
}

interface SseChunk {
  type: 'step' | 'tool_call' | 'result' | 'html_output' | 'file_output' | 'error';
  text?: string;
  toolName?: string;
  base64?: string;
  fileName?: string;
  downloadUrl?: string;
}

const SYSTEM_PROMPT = `You are FAIT Cowork — an AI assistant at Fortress Asset Management.
You complete business tasks for non-technical users: creating HTML prototypes, drafting documents,
summarizing files, and analyzing data.

Your working directory contains the user's uploaded files. You create output files there.
Explain each step as you work — users see your progress in real time.

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

  try {
    for await (const message of query({
      prompt: params.prompt,
      options: {
        cwd: params.workingDir,
        allowedTools: ['Read', 'Write', 'Edit', 'Bash'],
        maxBudgetUsd: params.maxBudgetUsd,
        maxTurns: params.maxTurns,
        systemPrompt,
        env: {
          // SAFE non-secret identifiers only — secrets must NEVER be in here
          // The Agent SDK may expose this env to Claude via Bash tool
          COWORK_TASK_ID:    params.taskId,
          COWORK_USER_ID:    params.userId,
          COWORK_USER_EMAIL: params.userEmail,
        },
      },
    })) {
      if (message.type === 'result') {
        const resultMsg = message as SDKResultSuccess;

        // Audit pre-result tool calls aren't tracked here — hook API requires
        // separate HookCallbackMatcher setup; audit is handled via CloudWatch
        const outputs = await collectOutputFiles(params.workingDir);
        for (const chunk of outputs) yield chunk;

        await auditLog({ event: 'task_completed', taskId: params.taskId, userId: params.userId });
        yield { type: 'result', text: resultMsg.result };
      } else if (message.type === 'assistant') {
        const assistantMsg = message as SDKAssistantMessage;
        for (const block of assistantMsg.message.content ?? []) {
          if (block.type === 'text' && block.text?.trim()) {
            yield { type: 'step', text: block.text };
          } else if (block.type === 'tool_use') {
            await auditLog({
              event: 'tool_call',
              taskId: params.taskId,
              userId: params.userId,
              data: { tool: block.name, input: safeSerialize(block.input) },
            });
            yield { type: 'tool_call', toolName: block.name, text: describeToolCall(block) };
          }
        }
      }
    }
  } catch (error: any) {
    await auditLog({ event: 'task_failed', taskId: params.taskId, userId: params.userId, data: { error: error.message } });
    yield { type: 'error', text: error.message ?? 'Task failed' };
  }
}

async function collectOutputFiles(workingDir: string): Promise<SseChunk[]> {
  const chunks: SseChunk[] = [];
  try {
    const entries = await fs.readdir(workingDir, { withFileTypes: true });
    for (const entry of entries) {
      if (!entry.isFile()) continue;
      const filePath = path.join(workingDir, entry.name);

      if (entry.name.endsWith('.html')) {
        const content = await fs.readFile(filePath, 'utf-8');
        chunks.push({
          type: 'html_output',
          base64: Buffer.from(content).toString('base64'),
          fileName: entry.name,
        });
      }

      chunks.push({
        type: 'file_output',
        fileName: entry.name,
        downloadUrl: `/tasks/files/${path.basename(workingDir)}/${entry.name}`,
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
  try {
    return JSON.parse(JSON.stringify(input));
  } catch {
    return String(input);
  }
}
