import { createClient } from 'redis';
export declare function getRedis(): Promise<ReturnType<typeof createClient>>;
export declare const TASK_TTL_SECONDS: number;
export declare const APPROVAL_TIMEOUT_MS: number;
export interface TaskMeta {
    status: 'running' | 'completed' | 'failed' | 'queued' | 'cancelled';
    userId: string;
    userEmail: string;
    prompt: string;
    createdAt: string;
    completedAt: string;
    outputFiles: string;
}
export declare function createTaskMeta(taskId: string, meta: Omit<TaskMeta, 'status' | 'completedAt' | 'outputFiles'>): Promise<void>;
export declare function updateTaskComplete(taskId: string, outputFiles: object[]): Promise<void>;
export declare function updateTaskFailed(taskId: string): Promise<void>;
export declare function getTaskMeta(taskId: string): Promise<TaskMeta | null>;
export declare function getUserTaskIds(userId: string, limit?: number): Promise<string[]>;
export declare function waitForApproval(approvalId: string): Promise<'approve' | 'reject'>;
export declare function setApprovalDecision(approvalId: string, decision: 'approve' | 'reject'): Promise<void>;
export declare function taskChannel(taskId: string): string;
export declare function publishChunk(taskId: string, chunk: object): Promise<void>;
export declare function subscribeToTask(taskId: string): AsyncGenerator<object>;
