import { S3Client, GetObjectCommand } from '@aws-sdk/client-s3'
import { LRUCache } from 'lru-cache'

const BUCKET = process.env.TEMPLATE_BUCKET || 'fip-proposal-templates'

const s3 = new S3Client({ region: process.env.AWS_REGION || 'us-east-1' })

export const LOB_PARTIAL_MAP = new Map([
  ['GeneralLiability',      'general-liability.docx'],
  ['WorkersCompensation',   'workers-compensation.docx'],
  ['CommercialProperty',    'commercial-property.docx'],
  ['CommercialAuto',        'commercial-auto.docx'],
  ['InlandMarine',          'inland-marine.docx'],
  ['Umbrella',              'umbrella.docx'],
  ['Excess',                'umbrella.docx'],
  ['Cyber',                 'cyber.docx'],
  ['DirectorsOfficers',     'directors-officers.docx'],
  ['EmploymentPractices',   'employment-practices.docx'],
  ['ManagementLiability',   'management-liability.docx'],
  ['ProfessionalLiability', 'professional-liability.docx'],
  ['Crime',                 'crime.docx'],
  ['ForeignPackage',        null],
  ['KidnapRansom',          'kidnap-ransom.docx'],
  ['ParticipantAccident',   'participant-accident.docx'],
  ['ActiveAssailant',       'active-assailant.docx'],
  ['Pollution',             'pollution.docx'],
  ['BuildersRisk',          'builders-risk.docx'],
  ['Other',                 'other.docx'],
])

const cache = new LRUCache({ max: 20, ttl: 5 * 60 * 1000 })

export function clearCache() {
  cache.clear()
}

async function streamToBuffer(stream) {
  const chunks = []
  for await (const chunk of stream) {
    chunks.push(chunk instanceof Buffer ? chunk : Buffer.from(chunk))
  }
  return Buffer.concat(chunks)
}

async function fetchS3Buffer(key) {
  const cached = cache.get(key)
  if (cached) return cached

  const response = await s3.send(new GetObjectCommand({ Bucket: BUCKET, Key: key }))
  const buf = await streamToBuffer(response.Body)
  cache.set(key, buf)
  return buf
}

async function fetchS3Json(key) {
  const buf = await fetchS3Buffer(key)
  return JSON.parse(buf.toString('utf-8'))
}

export async function loadTemplate(templateId, quotes, templateConfig) {
  const safeQuotes = quotes || []

  // 1. Load meta.json
  let meta
  try {
    meta = await fetchS3Json(`verticals/${templateId}/meta.json`)
  } catch (err) {
    const e = new Error(`Template '${templateId}' not found`)
    e.code = 'TEMPLATE_NOT_FOUND'
    e.statusCode = 400
    throw e
  }

  if (meta.active !== true) {
    const e = new Error(`Template '${templateId}' not found`)
    e.code = 'TEMPLATE_NOT_FOUND'
    e.statusCode = 400
    throw e
  }

  // 2. Load master.docx
  let masterDocx
  try {
    masterDocx = await fetchS3Buffer(`verticals/${templateId}/master.docx`)
  } catch (err) {
    const e = new Error(`Template '${templateId}' not found`)
    e.code = 'TEMPLATE_NOT_FOUND'
    e.statusCode = 400
    throw e
  }

  // 3. Load LOB partials
  const uniqueLobs = [...new Set(safeQuotes.map(q => q.lineOfBusiness))]
  const lobPartials = new Map()

  for (const lob of uniqueLobs) {
    if (!LOB_PARTIAL_MAP.has(lob)) {
      const e = new Error(`No LOB partial registered for '${lob}'`)
      e.code = 'LOB_PARTIAL_MISSING'
      e.statusCode = 400
      throw e
    }

    const lobKey = LOB_PARTIAL_MAP.get(lob)
    if (lobKey === null) {
      console.warn(`ForeignPackage LOB is parked — skipping partial`)
      continue
    }

    try {
      const buf = await fetchS3Buffer(`lob-partials/${lobKey}`)
      lobPartials.set(lob, buf)
    } catch (err) {
      if (err.name === 'NoSuchKey' || err.$metadata?.httpStatusCode === 404) {
        const e = new Error(`No LOB partial registered for '${lob}'`)
        e.code = 'LOB_PARTIAL_MISSING'
        e.statusCode = 400
        throw e
      }
      throw err
    }
  }

  // 4. Load boilerplate registry
  let boilerplateRegistry = { blocks: {} }
  try {
    boilerplateRegistry = await fetchS3Json('registry/boilerplate.json')
  } catch (err) {
    // non-fatal
  }

  // 5. Resolve selectedBoilerplate
  let selectedBoilerplate = templateConfig?.boilerplateSelections ?? meta.defaultBoilerplate ?? []
  const exclusions = templateConfig?.boilerplateExclusions || []
  if (exclusions.length > 0) {
    selectedBoilerplate = selectedBoilerplate.filter(k => !exclusions.includes(k))
  }

  return {
    meta,
    masterDocx,
    lobPartials,
    boilerplateRegistry,
    selectedBoilerplate,
  }
}
