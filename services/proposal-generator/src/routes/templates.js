import { createStorageProvider } from '../config.js'

const storageProvider = await createStorageProvider()

export default async function templatesRoute(fastify, options) {
  fastify.get('/', async (request, reply) => {
    let keys = []
    try {
      keys = await storageProvider.listKeys(
        storageProvider.templateBucket,
        `${storageProvider.templatePrefix}/verticals/`
      )
      const templatePrefix = storageProvider.templatePrefix
      keys = keys.filter(key => new RegExp(`^${templatePrefix}/verticals/[^/]+/meta\\.json$`).test(key))
    } catch (err) {
      fastify.log.warn({ err }, 'Could not list templates from S3 — bucket may not exist yet')
      return reply.send({ templates: [] })
    }

    const templates = []
    for (const key of keys) {
      try {
        const meta = await storageProvider.getJson(storageProvider.templateBucket, key)
        if (meta.active === true) {
          templates.push({
            templateId: meta.templateId,
            displayName: meta.displayName,
            vertical: meta.vertical,
            version: meta.version,
            lobPartials: meta.lobPartials || [],
            active: meta.active,
            updatedAt: meta.updatedAt,
          })
        }
      } catch (err) {
        fastify.log.warn({ key, err }, 'Failed to load meta.json for template')
      }
    }

    return reply.send({ templates })
  })
}
