"use strict";
var __importDefault = (this && this.__importDefault) || function (mod) {
    return (mod && mod.__esModule) ? mod : { "default": mod };
};
Object.defineProperty(exports, "__esModule", { value: true });
exports.uploadOutputToS3 = uploadOutputToS3;
exports.uploadInputsToS3 = uploadInputsToS3;
exports.downloadInputsFromS3 = downloadInputsFromS3;
const client_s3_1 = require("@aws-sdk/client-s3");
const s3_request_presigner_1 = require("@aws-sdk/s3-request-presigner");
const promises_1 = __importDefault(require("fs/promises"));
const path_1 = __importDefault(require("path"));
const s3 = new client_s3_1.S3Client({ region: process.env.AWS_REGION ?? 'us-east-1' });
const BUCKET = process.env.COWORK_S3_BUCKET ?? 'fip-cowork-workspaces';
const PRESIGN_TTL_SECONDS = 900; // 15 minutes
/** Upload a local output file to S3 and return a pre-signed download URL. */
async function uploadOutputToS3(localPath, taskId, fileName) {
    const key = `tasks/${taskId}/output/${fileName}`;
    const body = await promises_1.default.readFile(localPath);
    await s3.send(new client_s3_1.PutObjectCommand({
        Bucket: BUCKET,
        Key: key,
        Body: body,
        ServerSideEncryption: 'AES256',
    }));
    return (0, s3_request_presigner_1.getSignedUrl)(s3, new client_s3_1.GetObjectCommand({ Bucket: BUCKET, Key: key }), {
        expiresIn: PRESIGN_TTL_SECONDS,
    });
}
/** Upload input files (from multer) to S3 and clean up temp files. */
async function uploadInputsToS3(files, taskId) {
    for (const file of files) {
        const key = `tasks/${taskId}/input/${file.originalname}`;
        const body = await promises_1.default.readFile(file.path);
        await s3.send(new client_s3_1.PutObjectCommand({
            Bucket: BUCKET,
            Key: key,
            Body: body,
            ServerSideEncryption: 'AES256',
        }));
        await promises_1.default.unlink(file.path); // Remove multer temp file
    }
}
/** Download all input files from S3 to the task working directory. */
async function downloadInputsFromS3(taskId, workingDir) {
    const list = await s3.send(new client_s3_1.ListObjectsV2Command({
        Bucket: BUCKET,
        Prefix: `tasks/${taskId}/input/`,
    }));
    for (const obj of list.Contents ?? []) {
        if (!obj.Key)
            continue;
        const resp = await s3.send(new client_s3_1.GetObjectCommand({ Bucket: BUCKET, Key: obj.Key }));
        const fileName = path_1.default.basename(obj.Key);
        const localPath = path_1.default.join(workingDir, fileName);
        const body = resp.Body;
        const chunks = [];
        for await (const chunk of body)
            chunks.push(Buffer.from(chunk));
        await promises_1.default.writeFile(localPath, Buffer.concat(chunks));
    }
}
//# sourceMappingURL=fileService.js.map