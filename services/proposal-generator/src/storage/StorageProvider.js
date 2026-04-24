// src/storage/StorageProvider.js

/**
 * StorageProvider — base class for storage backends.
 *
 * Implementing classes MUST set these properties in their constructor:
 * @property {string} templateBucket — bucket/container holding templates
 * @property {string} templatePrefix — key prefix for templates (e.g. 'fip-proposal-templates')
 * @property {string} outputBucket — bucket/container for generated proposals
 * @property {string} outputPrefix — key prefix for output (e.g. 'proposals')
 * @property {number} signedUrlExpiry — signed URL expiry in seconds (e.g. 7200)
 *
 * Abstract methods (must implement):
 * - getTemplate(key)
 * - getTemplateMetadata(templateId)
 * - getBoilerplateRegistry()
 * - putProposal(key, buffer, contentType)
 * - getSignedUrl(key, expiresInSeconds)
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
