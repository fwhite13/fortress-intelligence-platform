// src/services/templateLoader.js

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

export function clearCache(storageProvider) {
  if (storageProvider?.clearCache) storageProvider.clearCache()
}

/**
 * Load template assets via storageProvider.
 *
 * @param {string} templateId
 * @param {Array} quotes
 * @param {object} templateConfig
 * @param {StorageProvider} storageProvider
 */
export async function loadTemplate(templateId, quotes, templateConfig, storageProvider) {
  const safeQuotes = quotes || []
  const templatePrefix = storageProvider.templatePrefix

  // 1. Resolve meta.json
  let meta
  try {
    meta = await storageProvider.getTemplateMetadata(templateId)
    if (!meta) throw new Error('not found')
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
  const masterKey = meta.s3Key
    ? `${templatePrefix}/${meta.s3Key}`
    : `${templatePrefix}/verticals/${meta.vertical || templateId}/master.docx`

  let masterDocx
  try {
    masterDocx = await storageProvider.getTemplate(masterKey)
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
      const buf = await storageProvider.getBuffer(
        storageProvider.templateBucket,
        `${templatePrefix}/lob-partials/${lobKey}`
      )
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
  const boilerplateRegistry = await storageProvider.getBoilerplateRegistry()

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
