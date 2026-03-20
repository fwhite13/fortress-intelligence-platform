/**
 * Attempt to start a task atomically.
 * Returns 'started' if a slot is available, 'queued' if at concurrency limit.
 * Uses Lua eval to atomically check count + increment (no TOCTOU race).
 */
export declare function tryStartTask(taskId: string, userId: string): Promise<'started' | 'queued'>;
/**
 * Called when a task finishes (completed, failed, or cancelled).
 * Decrements running count with floor at 0, promotes next queued task.
 * Returns the promoted taskId, or null if queue was empty.
 */
export declare function onTaskFinished(userId: string): Promise<string | null>;
/**
 * Cancel a task — removes from queue if queued, signals cancellation if running.
 */
export declare function cancelTask(taskId: string, userId: string): Promise<void>;
/**
 * Get the 1-based queue position for a queued task, or null if not queued.
 */
export declare function getQueuePosition(taskId: string, userId: string): Promise<number | null>;
