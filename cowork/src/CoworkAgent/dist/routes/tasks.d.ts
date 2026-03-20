declare const router: import("express-serve-static-core").Router;
export type OutputType = 'html' | 'markdown' | 'csv' | 'docx' | 'txt' | 'other';
export interface SseChunk {
    type: 'step' | 'tool_call' | 'result' | 'file_output' | 'approval_required' | 'approval_resolved' | 'error' | 'queued';
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
    position?: number;
}
export { router as tasksRouter };
