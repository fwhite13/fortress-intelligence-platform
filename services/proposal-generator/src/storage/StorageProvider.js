// src/storage/StorageProvider.js

/**
 * Abstract base class for storage providers.
 * Implement this interface to support different storage backends (S3, Azure Blob, etc.)
 */
export class StorageProvider {
  /** @returns {Promise<Buffer>} */
  async getBuffer(bucket, key) { throw new Error('Not implemented') }

  /** @returns {Promise<object>} parsed JSON */
  async getJson(bucket, key) { throw new Error('Not implemented') }

  /** @returns {Promise<string[]>} list of matching keys */
  async listKeys(bucket, prefix) { throw new Error('Not implemented') }

  /** @returns {Promise<Buffer>} template .docx buffer */
  async getTemplate(key) { throw new Error('Not implemented') }

  /** @returns {Promise<object>} parsed JSON */
  async getTemplateMetadata(templateId) { throw new Error('Not implemented') }

  /** @returns {Promise<object>} parsed boilerplate registry JSON */
  async getBoilerplateRegistry() { throw new Error('Not implemented') }

  /** @returns {Promise<void>} */
  async putProposal(key, buffer, contentType) { throw new Error('Not implemented') }

  /** @returns {Promise<string>} presigned download URL */
  async getSignedUrl(key, expiresInSeconds) { throw new Error('Not implemented') }
}
