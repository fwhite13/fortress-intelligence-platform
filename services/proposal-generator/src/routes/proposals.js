import { readFileSync } from 'fs'
import { fileURLToPath } from 'url'
import { dirname, join } from 'path'
import { ulid } from 'ulid'
import { loadTemplate } from '../services/templateLoader.js'

const __dirname = dirname(fileURLToPath(import.meta.url))
const schema = JSON.parse(
  readFileSync(join(__dirname, '../schemas/proposal-generator-schema.json'), 'utf-8')
)

// Strip $schema to prevent AJV from trying to fetch draft-07 meta-schema
const { $schema, $id, ...bodySchema } = schema

export default async function proposalsRoute(fastify, options) {
  fastify.post('/generate', {
    schema: {
      body: bodySchema,
    },
  }, async (request, reply) => {
    const payload = request.body

    const proposalId = 'prop_' + ulid()

    const result = await loadTemplate(
      payload.templateId,
      payload.quotes || [],
      payload.templateConfig
    )

    return reply.code(200).send({
      proposalId,
      templateId: payload.templateId,
      templateVersion: result.meta.version,
      status: 'stub',
    })
  })
}
