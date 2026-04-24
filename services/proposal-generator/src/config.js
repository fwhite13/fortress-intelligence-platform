// src/config.js

/**
 * AWS DEPENDENCY AUDIT (for Azure migration reference)
 * =====================================================
 * Packages:
 *   - @aws-sdk/client-s3 (S3StorageProvider)
 *   - @aws-sdk/s3-request-presigner (presigned URLs)
 *
 * Environment Variables (AWS-specific):
 *   - AWS_REGION (default: us-east-1)
 *   - AWS_PROFILE (local dev only)
 *   - TEMPLATE_BUCKET (default: fortress-tools)
 *   - TEMPLATE_PREFIX (default: fip-proposal-templates)
 *   - OUTPUT_BUCKET (default: fortress-tools)
 *   - OUTPUT_PREFIX (default: proposals)
 *
 * IAM Assumptions (ECS task role):
 *   - s3:GetObject on fortress-tools bucket (template reads)
 *   - s3:PutObject on fortress-tools bucket (proposal writes)
 *   - s3:GetObject for presigned URL generation
 *   - s3:ListObjectsV2 on fortress-tools bucket (template discovery)
 *
 * Service Endpoints:
 *   - s3.us-east-1.amazonaws.com (S3 API)
 *   - s3.amazonaws.com (presigned URL base)
 *
 * Azure Migration Notes:
 *   - Replace S3StorageProvider with AzureBlobStorageProvider
 *   - SAS tokens replace presigned URLs (similar concept)
 *   - @azure/storage-blob replaces @aws-sdk/client-s3
 *   - Managed Identity replaces IAM task role
 * =====================================================
 */

export const config = {
  storageProvider: process.env.STORAGE_PROVIDER || 's3',
  aws: {
    region: process.env.AWS_REGION || 'us-east-1',
  },
  storage: {
    templateBucket: process.env.TEMPLATE_BUCKET || 'fortress-tools',
    templatePrefix: process.env.TEMPLATE_PREFIX || 'fip-proposal-templates',
    outputBucket: process.env.OUTPUT_BUCKET || 'fortress-tools',
    outputPrefix: process.env.OUTPUT_PREFIX || 'proposals',
    signedUrlExpiry: parseInt(process.env.SIGNED_URL_EXPIRY_SECONDS || '7200', 10),
  },
  libreoffice: {
    path: process.env.SOFFICE_PATH || 'soffice',
    timeoutMs: parseInt(process.env.SOFFICE_TIMEOUT_MS || '30000', 10),
  },
}

export async function createStorageProvider() {
  if (config.storageProvider === 's3') {
    const { S3StorageProvider } = await import('./storage/S3StorageProvider.js')
    return new S3StorageProvider({
      region: config.aws.region,
      templateBucket: config.storage.templateBucket,
      templatePrefix: config.storage.templatePrefix,
      outputBucket: config.storage.outputBucket,
      outputPrefix: config.storage.outputPrefix,
      signedUrlExpiry: config.storage.signedUrlExpiry,
    })
  }
  throw new Error(`Unknown storage provider: ${config.storageProvider}`)
}
