import { readFileSync } from 'fs'
import { fileURLToPath } from 'url'
import { dirname, join } from 'path'
import { S3Client } from '@aws-sdk/client-s3'
import { renderDocument } from '../services/documentRenderer.js'

const __dirname = dirname(fileURLToPath(import.meta.url))
const schema = JSON.parse(
  readFileSync(join(__dirname, '../schemas/proposal-generator-schema.json'), 'utf-8')
)

// Strip $schema to prevent AJV from trying to fetch draft-07 meta-schema
const { $schema, $id, ...bodySchema } = schema

const s3Client = new S3Client({ region: process.env.AWS_REGION || 'us-east-1' })

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
      result = await renderDocument(payload, s3Client, logger)
    } catch (err) {
      if (err.code === 'GENERATION_FAILED') {
        return reply.code(500).send({
          error: 'GENERATION_FAILED',
          message: err.message || 'Template rendering failed',
        })
      }
      // TEMPLATE_NOT_FOUND and LOB_PARTIAL_MISSING are caught by the error handler
      throw err
    }

    reply.header('Content-Type', 'application/vnd.openxmlformats-officedocument.wordprocessingml.document')
    reply.header('Content-Disposition', `attachment; filename="${result.proposalNumber}.docx"`)
    return reply.code(200).send(result.docxBuffer)
  })
}
