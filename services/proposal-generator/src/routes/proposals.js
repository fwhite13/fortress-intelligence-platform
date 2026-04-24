import { readFileSync } from 'fs'
import { fileURLToPath } from 'url'
import { dirname, join } from 'path'
import { createStorageProvider } from '../config.js'
import { renderDocument } from '../services/documentRenderer.js'

const __dirname = dirname(fileURLToPath(import.meta.url))
const schema = JSON.parse(
  readFileSync(join(__dirname, '../schemas/proposal-generator-schema.json'), 'utf-8')
)

const { $schema, $id, ...bodySchema } = schema

// Initialize storage provider once at startup
const storageProvider = await createStorageProvider()

export default async function proposalsRoute(fastify, options) {
  fastify.post('/generate', {
    schema: {
      body: bodySchema,
    },
  }, async (request, reply) => {
    const payload = request.body
    const logger = request.log

    let result
    try {
      result = await renderDocument(payload, storageProvider, logger)
    } catch (err) {
      if (err.code === 'GENERATION_FAILED') {
        return reply.code(500).send({
          error: 'GENERATION_FAILED',
          message: err.message || 'Template rendering failed',
        })
      }
      throw err
    }

    return reply.code(200).send({
      proposalId: result.proposalId,
      proposalNumber: result.proposalNumber,
      templateId: payload.templateId,
      templateVersion: result.templateVersion,
      downloadUrl: result.outputs.docx?.downloadUrl || null,
      downloadUrlPdf: result.outputs.pdf?.downloadUrl || null,
      downloadUrlExpiresAt: result.outputs.docx?.expiresAt || result.outputs.pdf?.expiresAt || null,
      generatedAt: new Date().toISOString(),
      warnings: result.warnings || [],
    })
  })
}
