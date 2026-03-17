import { S3Client, PutObjectCommand, GetObjectCommand, ListObjectsV2Command } from '@aws-sdk/client-s3';
import { getSignedUrl } from '@aws-sdk/s3-request-presigner';
import fs from 'fs/promises';
import path from 'path';

const s3     = new S3Client({ region: process.env.AWS_REGION ?? 'us-east-1' });
const BUCKET = process.env.COWORK_S3_BUCKET ?? 'fip-cowork-workspaces';
const PRESIGN_TTL_SECONDS = 900; // 15 minutes

/** Upload a local output file to S3 and return a pre-signed download URL. */
export async function uploadOutputToS3(
  localPath: string,
  taskId: string,
  fileName: string
): Promise<string> {
  const key  = `tasks/${taskId}/output/${fileName}`;
  const body = await fs.readFile(localPath);

  await s3.send(new PutObjectCommand({
    Bucket: BUCKET,
    Key:    key,
    Body:   body,
    ServerSideEncryption: 'AES256',
  }));

  return getSignedUrl(s3, new GetObjectCommand({ Bucket: BUCKET, Key: key }), {
    expiresIn: PRESIGN_TTL_SECONDS,
  });
}

/** Upload input files (from multer) to S3 and clean up temp files. */
export async function uploadInputsToS3(
  files: Express.Multer.File[],
  taskId: string
): Promise<void> {
  for (const file of files) {
    const key  = `tasks/${taskId}/input/${file.originalname}`;
    const body = await fs.readFile(file.path);
    await s3.send(new PutObjectCommand({
      Bucket: BUCKET,
      Key:    key,
      Body:   body,
      ServerSideEncryption: 'AES256',
    }));
    await fs.unlink(file.path); // Remove multer temp file
  }
}

/** Download all input files from S3 to the task working directory. */
export async function downloadInputsFromS3(taskId: string, workingDir: string): Promise<void> {
  const list = await s3.send(new ListObjectsV2Command({
    Bucket: BUCKET,
    Prefix: `tasks/${taskId}/input/`,
  }));

  for (const obj of list.Contents ?? []) {
    if (!obj.Key) continue;
    const resp     = await s3.send(new GetObjectCommand({ Bucket: BUCKET, Key: obj.Key }));
    const fileName = path.basename(obj.Key);
    const localPath = path.join(workingDir, fileName);

    const body   = resp.Body as NodeJS.ReadableStream;
    const chunks: Buffer[] = [];
    for await (const chunk of body) chunks.push(Buffer.from(chunk));
    await fs.writeFile(localPath, Buffer.concat(chunks));
  }
}
