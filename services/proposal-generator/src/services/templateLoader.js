import { S3Client, GetObjectCommand, ListObjectsV2Command } from '@aws-sdk/client-s3'
import { LRUCache } from 'lru-cache'

export const TEMPLATE_BUCKET = process.env.TEMPLATE_BUCKET || 'fortress-tools'
export const TEMPLATE_PREFIX = process.env.TEMPLATE_PREFIX || 'fip-proposal-templates'

export const s3 = new S3Client({ region: process.env.AWS_REGION || 'us-east-1' })

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

  const response = await s3.send(new GetObjectCommand({ Bucket: TEMPLATE_BUCKET, Key: key }))
  const buf = await streamToBuffer(response.Body)
  cache.set(key, buf)
  return buf
}

async function fetchS3Json(key) {
  const buf = await fetchS3Buffer(key)
  return JSON.parse(buf.toString('utf-8'))
}

/**
 * Resolve templateId to its meta.json S3 key by scanning the verticals/ prefix.
 * Folder name may differ from templateId (e.g. folder='nba', templateId='nba-v1').
 * Result is cached for the LRU TTL.
 */
async function resolveTemplateMetaKey(templateId) {
  const cacheKey = `__meta_key__${templateId}`
  const cached = cache.get(cacheKey)
  if (cached) return cached

  const listResult = await s3.send(new ListObjectsV2Command({
    Bucket: TEMPLATE_BUCKET,
    Prefix: `${TEMPLATE_PREFIX}/verticals/`,
  }))

  const metaKeys = (listResult.Contents || [])
    .map(obj => obj.Key)
    .filter(key => /\/meta\.json$/.test(key))

  for (const key of metaKeys) {
    const meta = await fetchS3Json(key)
    if (meta.templateId === templateId) {
      cache.set(cacheKey, key)
      return key
    }
  }

  return null
}

export async function loadTemplate(templateId, quotes, templateConfig) {
  const safeQuotes = quotes || []

  // 1. Resolve meta.json path (folder name may differ from templateId)
  let meta
  let metaKey
  try {
    metaKey = await resolveTemplateMetaKey(templateId)
    if (!metaKey) throw new Error('not found')
    meta = await fetchS3Json(metaKey)
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

  // 2. Load master.docx — use s3Key from meta if present, else fall back to vertical folder
  const masterKey = meta.s3Key
    ? `${TEMPLATE_PREFIX}/${meta.s3Key}`
    : `${TEMPLATE_PREFIX}/verticals/${meta.vertical || templateId}/master.docx`

  let masterDocx
  try {
    masterDocx = await fetchS3Buffer(masterKey)
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
      const buf = await fetchS3Buffer(`${TEMPLATE_PREFIX}/lob-partials/${lobKey}`)
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
    boilerplateRegistry = await fetchS3Json(`${TEMPLATE_PREFIX}/registry/boilerplate.json`)
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
