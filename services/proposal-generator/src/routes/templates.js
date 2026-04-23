import { S3Client, ListObjectsV2Command, GetObjectCommand } from '@aws-sdk/client-s3'

const BUCKET = process.env.TEMPLATE_BUCKET || 'fip-proposal-templates'
const s3 = new S3Client({ region: process.env.AWS_REGION || 'us-east-1' })

async function streamToString(stream) {
  const chunks = []
  for await (const chunk of stream) {
    chunks.push(typeof chunk === 'string' ? Buffer.from(chunk) : chunk)
  }
  return Buffer.concat(chunks).toString('utf-8')
}

export default async function templatesRoute(fastify, options) {
  fastify.get('/', async (request, reply) => {
    let keys = []
    try {
      const listResult = await s3.send(new ListObjectsV2Command({
        Bucket: BUCKET,
        Prefix: 'verticals/',
      }))
      keys = (listResult.Contents || [])
        .map(obj => obj.Key)
        .filter(key => /^verticals\/[^/]+\/meta\.json$/.test(key))
    } catch (err) {
      fastify.log.warn({ err }, 'Could not list templates from S3 — bucket may not exist yet')
      return reply.send({ templates: [] })
    }

    const templates = []
    for (const key of keys) {
      try {
        const getResult = await s3.send(new GetObjectCommand({ Bucket: BUCKET, Key: key }))
        const text = await streamToString(getResult.Body)
        const meta = JSON.parse(text)
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
