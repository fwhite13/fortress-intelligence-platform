import Fastify from 'fastify'
import cors from '@fastify/cors'
import { readFileSync } from 'fs'
import { fileURLToPath } from 'url'
import { dirname, join } from 'path'
import proposalsRoute from './routes/proposals.js'
import templatesRoute from './routes/templates.js'

const __dirname = dirname(fileURLToPath(import.meta.url))
const PORT = parseInt(process.env.PORT || '3000', 10)

const app = Fastify({
  logger: {
    level: process.env.LOG_LEVEL || 'info',
  },
  ajv: {
    customOptions: {
      allErrors: true,
      coerceTypes: false,
      useDefaults: false,
      strict: false,
    },
  },
})

await app.register(cors)

await app.register(proposalsRoute, { prefix: '/proposals' })
await app.register(templatesRoute, { prefix: '/templates' })

app.setErrorHandler((error, request, reply) => {
  if (error.validation) {
    const details = error.validation.map((v) => ({
      field: v.instancePath ? v.instancePath.replace(/^\//, '').replace(/\//g, '.') : v.params?.missingProperty || 'unknown',
      message: v.message || 'Validation error',
    }))
    return reply.code(400).send({
      error: 'VALIDATION_ERROR',
      message: 'Request validation failed',
      details,
    })
  }

  if (error.code === 'TEMPLATE_NOT_FOUND' || error.code === 'LOB_PARTIAL_MISSING') {
    return reply.code(400).send({
      error: error.code,
      message: error.message,
      details: [],
    })
  }

  app.log.error(error)
  return reply.code(500).send({
    error: 'INTERNAL_ERROR',
    message: 'An unexpected error occurred',
  })
})

app.get('/health', async (request, reply) => {
  return { status: 'ok', version: '1.0.0' }
})

const shutdown = async (signal) => {
  app.log.info(`${signal} received — shutting down`)
  await app.close()
  process.exit(0)
}

process.on('SIGTERM', () => shutdown('SIGTERM'))
process.on('SIGINT', () => shutdown('SIGINT'))

try {
  await app.listen({ port: PORT, host: '0.0.0.0' })
  app.log.info(`Proposal Generator listening on port ${PORT}`)
} catch (err) {
  app.log.error(err)
  process.exit(1)
}
