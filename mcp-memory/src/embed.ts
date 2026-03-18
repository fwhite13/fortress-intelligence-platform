import { BedrockRuntimeClient, InvokeModelCommand } from '@aws-sdk/client-bedrock-runtime';

const client = new BedrockRuntimeClient({ region: process.env.AWS_REGION || 'us-east-1' });
const MODEL_ID = 'amazon.titan-embed-text-v2:0';

export async function embedText(text: string): Promise<number[]> {
  const body = JSON.stringify({ inputText: text.slice(0, 8000) });
  const cmd = new InvokeModelCommand({
    modelId: MODEL_ID,
    contentType: 'application/json',
    accept: 'application/json',
    body: Buffer.from(body),
  });
  const response = await client.send(cmd);
  const result = JSON.parse(Buffer.from(response.body).toString('utf8')) as { embedding: number[] };
  return result.embedding;
}
