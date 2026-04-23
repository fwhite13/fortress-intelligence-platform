import Fastify from 'fastify'
import cors from '@fastify/cors'

const PORT = parseInt(process.env.PORT || '3000', 10)

const app = Fastify({
  logger: {
    level: 'info'
  }
})

await app.register(cors)

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
