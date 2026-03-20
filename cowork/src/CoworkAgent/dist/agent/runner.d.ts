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
    taskId: string;
    userId: string;
    userEmail: string;
    prompt: string;
    workingDir: string;
    maxBudgetUsd: number;
    maxTurns: number;
    systemPromptOverride?: string;
}
export declare function runTask(params: TaskParams): AsyncGenerator<SseChunk>;
export {};
