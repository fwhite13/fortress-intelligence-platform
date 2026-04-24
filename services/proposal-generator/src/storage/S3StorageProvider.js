// src/storage/S3StorageProvider.js
import { S3Client, GetObjectCommand, PutObjectCommand, ListObjectsV2Command } from '@aws-sdk/client-s3'
import { getSignedUrl as s3GetSignedUrl } from '@aws-sdk/s3-request-presigner'
import { LRUCache } from 'lru-cache'
import { StorageProvider } from './StorageProvider.js'

export class S3StorageProvider extends StorageProvider {
  constructor(config) {
    super()
    this.s3 = new S3Client({ region: config.region || process.env.AWS_REGION || 'us-east-1' })
    this.templateBucket = config.templateBucket
    this.templatePrefix = config.templatePrefix
    this.outputBucket = config.outputBucket
    this.outputPrefix = config.outputPrefix
    this.signedUrlExpiry = config.signedUrlExpiry || 7200
    this._cache = new LRUCache({ max: 20, ttl: 5 * 60 * 1000 })
  }

  async _streamToBuffer(stream) {
    const chunks = []
    for await (const chunk of stream) {
      chunks.push(chunk instanceof Buffer ? chunk : Buffer.from(chunk))
    }
    return Buffer.concat(chunks)
  }

  async getBuffer(bucket, key) {
    const cacheKey = `${bucket}::${key}`
    const cached = this._cache.get(cacheKey)
    if (cached) return cached

    const response = await this.s3.send(new GetObjectCommand({ Bucket: bucket, Key: key }))
    const buf = await this._streamToBuffer(response.Body)
    this._cache.set(cacheKey, buf)
    return buf
  }

  async getJson(bucket, key) {
    const buf = await this.getBuffer(bucket, key)
    return JSON.parse(buf.toString('utf-8'))
  }

  async listKeys(bucket, prefix) {
    const listResult = await this.s3.send(new ListObjectsV2Command({ Bucket: bucket, Prefix: prefix }))
    return (listResult.Contents || []).map(obj => obj.Key)
  }

  // Resolve templateId → meta.json S3 key by scanning verticals/ prefix
  async _resolveTemplateMetaKey(templateId) {
    const cacheKey = `__meta_key__${templateId}`
    const cached = this._cache.get(cacheKey)
    if (cached) return cached

    const keys = await this.listKeys(this.templateBucket, `${this.templatePrefix}/verticals/`)
    const metaKeys = keys.filter(key => /\/meta\.json$/.test(key))

    for (const key of metaKeys) {
      const meta = await this.getJson(this.templateBucket, key)
      if (meta.templateId === templateId) {
        this._cache.set(cacheKey, key)
        return key
      }
    }
    return null
  }

  async getTemplate(key) {
    return this.getBuffer(this.templateBucket, key)
  }

  async getTemplateMetadata(templateId) {
    const metaKey = await this._resolveTemplateMetaKey(templateId)
    if (!metaKey) return null
    return this.getJson(this.templateBucket, metaKey)
  }

  async getBoilerplateRegistry() {
    try {
      return await this.getJson(this.templateBucket, `${this.templatePrefix}/registry/boilerplate.json`)
    } catch (err) {
      return { blocks: {} }
    }
  }

  async putProposal(key, buffer, contentType) {
    await this.s3.send(new PutObjectCommand({
      Bucket: this.outputBucket,
      Key: key,
      Body: buffer,
      ContentType: contentType,
    }))
  }

  async getSignedUrl(key, expiresInSeconds) {
    return s3GetSignedUrl(
      this.s3,
      new GetObjectCommand({ Bucket: this.outputBucket, Key: key }),
      { expiresIn: expiresInSeconds || this.signedUrlExpiry }
    )
  }

  clearCache() {
    this._cache.clear()
  }
}
