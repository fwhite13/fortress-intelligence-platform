// src/services/s3Output.js
import { PutObjectCommand, GetObjectCommand } from '@aws-sdk/client-s3'
import { getSignedUrl } from '@aws-sdk/s3-request-presigner'

const OUTPUT_BUCKET = process.env.OUTPUT_BUCKET || 'fortress-tools'
const OUTPUT_PREFIX = process.env.OUTPUT_PREFIX || 'proposals'
const PRESIGNED_URL_EXPIRES_SECONDS = 2 * 60 * 60  // 2 hours

/**
 * Upload a generated proposal to S3 and return a presigned download URL.
 *
 * Key format: {OUTPUT_PREFIX}/{year}/{month}/{proposalId}.{ext}
 * Example: proposals/2026/04/prop_01ABC.docx
 *
 * @param {S3Client} s3Client
 * @param {Buffer} buffer
 * @param {string} proposalId
 * @param {string} extension - 'docx' or 'pdf'
 * @param {string} contentType
 * @returns {Promise<{ s3Key: string, downloadUrl: string, expiresAt: string }>}
 */
export async function uploadProposal(s3Client, buffer, proposalId, extension, contentType) {
  const now = new Date()
  const year = now.getFullYear()
  const month = String(now.getMonth() + 1).padStart(2, '0')
  const key = `${OUTPUT_PREFIX}/${year}/${month}/${proposalId}.${extension}`

  await s3Client.send(new PutObjectCommand({
    Bucket: OUTPUT_BUCKET,
    Key: key,
    Body: buffer,
    ContentType: contentType,
  }))

  const expiresAt = new Date(Date.now() + PRESIGNED_URL_EXPIRES_SECONDS * 1000).toISOString()

  const downloadUrl = await getSignedUrl(
    s3Client,
    new GetObjectCommand({ Bucket: OUTPUT_BUCKET, Key: key }),
    { expiresIn: PRESIGNED_URL_EXPIRES_SECONDS }
  )

  return { s3Key: key, downloadUrl, expiresAt }
}
