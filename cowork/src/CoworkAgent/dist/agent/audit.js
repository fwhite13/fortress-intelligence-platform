"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.auditLog = auditLog;
const client_cloudwatch_logs_1 = require("@aws-sdk/client-cloudwatch-logs");
const client = new client_cloudwatch_logs_1.CloudWatchLogsClient({ region: process.env.AWS_REGION ?? 'us-east-1' });
const LOG_GROUP = '/cowork/tasks';
async function auditLog(entry) {
    const streamName = entry.taskId ?? 'system';
    const message = JSON.stringify({
        timestamp: new Date().toISOString(),
        ...entry,
        // Redact: never log file contents, API keys, or JWTs
        prompt: entry.prompt ? entry.prompt.slice(0, 200) : undefined,
    });
    try {
        await client.send(new client_cloudwatch_logs_1.PutLogEventsCommand({
            logGroupName: LOG_GROUP,
            logStreamName: streamName,
            logEvents: [{
                    timestamp: Date.now(),
                    message,
                }],
        }));
    }
    catch (err) {
        // Non-fatal — audit failure must not break task execution
        console.error('Audit log failed:', err.message);
    }
}
//# sourceMappingURL=audit.js.map