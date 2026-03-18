"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.embedText = embedText;
const client_bedrock_runtime_1 = require("@aws-sdk/client-bedrock-runtime");
const client = new client_bedrock_runtime_1.BedrockRuntimeClient({ region: process.env.AWS_REGION || 'us-east-1' });
const MODEL_ID = 'amazon.titan-embed-text-v2:0';
async function embedText(text) {
    const body = JSON.stringify({ inputText: text.slice(0, 8000) });
    const cmd = new client_bedrock_runtime_1.InvokeModelCommand({
        modelId: MODEL_ID,
        contentType: 'application/json',
        accept: 'application/json',
        body: Buffer.from(body),
    });
    const response = await client.send(cmd);
    const result = JSON.parse(Buffer.from(response.body).toString('utf8'));
    return result.embedding;
}
