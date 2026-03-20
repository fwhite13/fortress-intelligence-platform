import type { SseChunk } from '../../agent/runner.js';
export interface DesignTaskParams {
    taskId: string;
    userId: string;
    userEmail: string;
    orgId: string;
    projectId: string;
    screenId?: string;
    priorHtml?: string;
    prompt: string;
    variantCount: 1 | 2 | 3;
    deviceTarget: 'mobile' | 'desktop' | 'responsive';
    convertToBlazor: boolean;
    referenceFiles?: string[];
}
export declare function runDesignTask(params: DesignTaskParams, emit: (chunk: SseChunk) => void): Promise<void>;
