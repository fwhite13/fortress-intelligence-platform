import { test, mock } from 'node:test'
import assert from 'node:assert/strict'

// Mock templateLoader BEFORE importing server/fastify app
mock.module('../src/services/templateLoader.js', {
  namedExports: {
    loadTemplate: async (templateId, quotes, templateConfig) => ({
      meta: { version: '1.2.0', active: true },
      masterDocx: Buffer.from('fake'),
      lobPartials: new Map(),
      boilerplateRegistry: { blocks: {} },
      selectedBoilerplate: [],
    }),
    clearCache: () => {},
    LOB_PARTIAL_MAP: new Map(),
  },
})

// Mock documentRenderer — the route now calls renderDocument instead of loadTemplate directly
mock.module('../src/services/documentRenderer.js', {
  namedExports: {
    renderDocument: async (payload, s3Client, logger) => ({
      proposalId: 'prop_TEST01234567890123456',
      proposalNumber: 'PROP-TEST',
      templateVersion: '1.2.0',
      outputFormat: 'docx',
      outputs: {
        docx: {
          s3Key: 'proposals/2026/04/prop_TEST01234567890123456.docx',
          downloadUrl: 'https://fortress-tools.s3.amazonaws.com/proposals/2026/04/prop_TEST01234567890123456.docx?fake',
          expiresAt: new Date(Date.now() + 2 * 60 * 60 * 1000).toISOString(),
        }
      },
      warnings: [],
    }),
  },
})

// Import Fastify and build test app AFTER mock
const { default: Fastify } = await import('fastify')
const { default: proposalsRoute } = await import('../src/routes/proposals.js')

async function buildApp() {
  const app = Fastify({
    ajv: {
      customOptions: {
        allErrors: true,
        coerceTypes: false,
        useDefaults: true, // useDefaults: true — AJV populates schema defaults so rendering code can trust them
        strict: false,
      },
    },
  })
  app.setErrorHandler((error, request, reply) => {
    if (error.validation) {
      const details = error.validation.map((v) => ({
        field: v.keyword === 'required'
          ? [
              v.instancePath.replace(/^\//, '').replace(/\//g, '.'),
              v.params?.missingProperty
            ].filter(Boolean).join('.')
          : v.instancePath.replace(/^\//, '').replace(/\//g, '.') || v.params?.missingProperty || 'unknown',
        message: v.message || 'Validation error',
      }))
      return reply.code(400).send({
        error: 'VALIDATION_ERROR',
        message: 'Request validation failed',
        details,
      })
    }
    if (error.code === 'TEMPLATE_NOT_FOUND' || error.code === 'LOB_PARTIAL_MISSING') {
      return reply.code(400).send({ error: error.code, message: error.message, details: [] })
    }
    reply.code(500).send({ error: 'INTERNAL_ERROR', message: 'An unexpected error occurred' })
  })
  await app.register(proposalsRoute, { prefix: '/proposals' })
  await app.ready()
  return app
}

const validPayload = {
  templateId: 'nba-v1',
  insured: {
    name: 'Acme Corp',
    address: {
      street1: '123 Main St',
      city: 'Springfield',
      state: 'IL',
      zip: '62701',
    },
  },
  policyPeriod: {
    effectiveDate: '2026-01-01',
    expirationDate: '2027-01-01',
  },
  quotes: [
    {
      lineOfBusiness: 'GeneralLiability',
      carrier: { name: 'Carrier A' },
      premium: 5000,
    },
  ],
}

test('POST /proposals/generate with valid payload → 200 JSON with presigned URL', async (t) => {
  const app = await buildApp()
  const response = await app.inject({
    method: 'POST',
    url: '/proposals/generate',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(validPayload),
  })
  assert.equal(response.statusCode, 200)
  const body = JSON.parse(response.body)
  assert.equal(body.proposalId, 'prop_TEST01234567890123456')
  assert.ok(body.downloadUrl.startsWith('https://'), 'downloadUrl should be a URL')
  await app.close()
})

test('POST /proposals/generate missing required fields → 400 VALIDATION_ERROR', async (t) => {
  const app = await buildApp()
  const response = await app.inject({
    method: 'POST',
    url: '/proposals/generate',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({}),
  })
  assert.equal(response.statusCode, 400)
  const body = JSON.parse(response.body)
  assert.equal(body.error, 'VALIDATION_ERROR')
  assert.ok(Array.isArray(body.details))
  assert.ok(body.details.length > 0)
  await app.close()
})

test('POST /proposals/generate invalid type → 400 VALIDATION_ERROR', async (t) => {
  const app = await buildApp()
  const response = await app.inject({
    method: 'POST',
    url: '/proposals/generate',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ ...validPayload, templateId: 123 }),
  })
  assert.equal(response.statusCode, 400)
  const body = JSON.parse(response.body)
  assert.equal(body.error, 'VALIDATION_ERROR')
  await app.close()
})

test('POST /proposals/generate with valid quotes → 200', async (t) => {
  const app = await buildApp()
  const payload = {
    ...validPayload,
    quotes: [
      { lineOfBusiness: 'Cyber', carrier: { name: 'Carrier B' }, premium: 3000 },
    ],
  }
  const response = await app.inject({
    method: 'POST',
    url: '/proposals/generate',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(payload),
  })
  assert.equal(response.statusCode, 200)
  const body = JSON.parse(response.body)
  assert.ok(body.downloadUrl, 'downloadUrl should be set')
  await app.close()
})

test('POST /proposals/generate nested missing field → details[].field = "insured.name"', async (t) => {
  const app = await buildApp()
  // Provide insured object but omit the required insured.name field
  const payload = {
    ...validPayload,
    insured: {
      address: {
        street1: '123 Main St',
        city: 'Springfield',
        state: 'IL',
        zip: '62701',
      },
    },
  }
  const response = await app.inject({
    method: 'POST',
    url: '/proposals/generate',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(payload),
  })
  assert.equal(response.statusCode, 400)
  const body = JSON.parse(response.body)
  assert.equal(body.error, 'VALIDATION_ERROR')
  const nameError = body.details.find((d) => d.field === 'insured.name')
  assert.ok(nameError, `Expected a detail with field "insured.name", got: ${JSON.stringify(body.details)}`)
  await app.close()
})
