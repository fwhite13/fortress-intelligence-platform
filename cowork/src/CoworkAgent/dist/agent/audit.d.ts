interface AuditEntry {
    event: string;
    taskId?: string;
    userId?: string;
    userEmail?: string;
    prompt?: string;
    data?: any;
}
export declare function auditLog(entry: AuditEntry): Promise<void>;
export {};
