// src/services/documentRenderer.js
import PizZip from 'pizzip'
import Docxtemplater from 'docxtemplater'
import { createRequire } from 'module'
import { ulid } from 'ulid'
import { loadTemplate } from './templateLoader.js'
import { postProcess } from './postProcessor.js'
import { renderLobPartial } from './lobRenderer.js'
import { renderBoilerplate } from './boilerplateRenderer.js'
import { assembleTemplateData, assembleNbaisWcTemplateData } from './assembleTemplateData.js'

const require = createRequire(import.meta.url)
const ImageModule = require('docxtemplater-image-module-free')

function generateProposalNumber() {
  const now = new Date()
  const year = now.getFullYear()
  const seq = Math.floor(Math.random() * 99999).toString().padStart(5, '0')
  return `PROP-${year}-${seq}`
}

/**
 * Try to load the vertical logo from S3.
 * Tries .png first, then .svg. Returns null if not found.
 */
async function loadLogo(storageProvider, templateId, logger) {
  for (const ext of ['png', 'svg']) {
    const key = `${storageProvider.templatePrefix}/verticals/${templateId}/logo.${ext}`
    try {
      return await storageProvider.getBuffer(storageProvider.templateBucket, key)
    } catch (err) {
      if (err.name !== 'NoSuchKey' && err.$metadata?.httpStatusCode !== 404) {
        logger?.warn({ templateId, ext, err: err.message }, 'Unexpected error loading logo — continuing without logo')
      }
      // continue to next extension
    }
  }
  logger?.debug({ templateId }, 'No vertical logo found — proceeding without logo')
  return null
}

/**
 * Load named logos from S3 for verticals that need multiple logo files.
 * Returns an object with named buffers: { stacked: Buffer, horizontal: Buffer }
 */
async function loadNamedLogos(storageProvider, templateId, logoConfig, logger) {
  if (!logoConfig) return null
  const result = {}
  for (const [name, filename] of Object.entries(logoConfig)) {
    const key = `${storageProvider.templatePrefix}/verticals/${templateId}/${filename}`
    try {
      result[name] = await storageProvider.getBuffer(storageProvider.templateBucket, key)
    } catch (err) {
      if (err.name !== 'NoSuchKey' && err.$metadata?.httpStatusCode !== 404) {
        logger?.warn({ templateId, name, err: err.message }, 'Unexpected error loading named logo')
      }
      result[name] = null
    }
  }
  return result
}

/**
 * Main rendering pipeline. Orchestrates template loading, LOB partial rendering,
 * boilerplate rendering, data assembly, and master template rendering.
 *
 * @param {Object} payload - validated request payload
 * @param {StorageProvider} storageProvider - storage provider instance
 * @param {Object} logger - pino logger
 * @returns {Promise<{ proposalId: string, proposalNumber: string, templateVersion: string, outputFormat: string, outputs: Object, warnings: string[] }>}
 */
export async function renderDocument(payload, storageProvider, logger) {
  const templateId = payload.templateId
  const quotes = payload.quotes || []
  const outputFormat = payload.outputFormat || 'docx'

  // Step 1: Load template (meta + master.docx + LOB partials + boilerplate registry)
  const { meta, masterDocx, lobPartials, boilerplateRegistry, selectedBoilerplate } = await loadTemplate(
    templateId,
    quotes,
    payload.templateConfig,
    storageProvider
  )

  // Step 2: Render LOB partials → collect body XML in order
  const lobXmlParts = []
  for (const quote of quotes) {
    const lob = quote.lineOfBusiness
    if (lob === 'ForeignPackage') {
      logger?.warn({ lob }, 'ForeignPackage LOB is parked — skipping rendering')
      continue
    }
    const lobBuf = lobPartials.get(lob)
    if (!lobBuf) {
      logger?.warn({ lob }, 'No LOB partial buffer found — skipping')
      continue
    }
    const xml = await renderLobPartial(quote, lobBuf, logger)
    lobXmlParts.push(xml)
  }
  const lobSectionsXml = lobXmlParts.join('\n')

  // Step 3: Build template data (needed for boilerplate variable substitution)
  // We'll assemble it twice — once for boilerplate vars (before boilerplate is rendered),
  // and once fully after. Use a preliminary version for boilerplate substitution.
  const resolvedProposalNumber = payload.proposalNumber || generateProposalNumber()
  const prelimTemplateData = {
    insuredName: payload.insured?.name || '',
    amName: payload.metadata?.amName || '',
    amEmail: payload.metadata?.amEmail || '',
    proposalNumber: resolvedProposalNumber,
    generatedDate: new Date().toLocaleDateString('en-US', { month: '2-digit', day: '2-digit', year: 'numeric' }),
  }

  // Step 4: Render boilerplate
  const exclusions = payload.templateConfig?.boilerplateExclusions || []
  const boilerplateSectionsXml = await renderBoilerplate(
    boilerplateRegistry,
    selectedBoilerplate,
    exclusions,
    meta.defaultBoilerplate,
    prelimTemplateData,
    storageProvider,
    logger
  )

  // Step 5: Load vertical logo(s) (graceful — null if not found)
  const isNbaisWc = meta.vertical === 'nbais-wc'
  let logoBuffer = null
  let namedLogos = null

  if (isNbaisWc && meta.logos) {
    namedLogos = await loadNamedLogos(storageProvider, templateId, meta.logos, logger)
  } else {
    logoBuffer = await loadLogo(storageProvider, templateId, logger)
  }

  // Step 6: Assemble full template data
  payload.proposalNumber = resolvedProposalNumber
  let templateData
  if (isNbaisWc) {
    templateData = assembleNbaisWcTemplateData(payload, meta, namedLogos, logger)
  } else {
    templateData = assembleTemplateData(payload, meta, logoBuffer, lobSectionsXml, boilerplateSectionsXml, logger)
  }

  // Step 7: Render master template
  let docxBuffer
  try {
    const imageModule = new ImageModule({
      centered: false,
      fileType: 'docx',
      getImage(tagValue) {
        if (!tagValue) return null
        return Buffer.from(tagValue, 'base64')
      },
      getSize() {
        return [150, 75] // default logo size in px — templates can override via placeholder options
      },
    })

    const zip = new PizZip(masterDocx)
    const doc = new Docxtemplater(zip, {
      paragraphLoop: true,
      linebreaks: true,
      modules: [imageModule],
      nullGetter(part) {
        return part.module ? null : ''
      },
    })

    doc.render(templateData)
    docxBuffer = doc.getZip().generate({ type: 'nodebuffer', compression: 'DEFLATE' })
  } catch (err) {
    logger?.error({ err: err.message }, 'docxtemplater rendering failed')
    const renderError = new Error(err.message || 'Template rendering failed')
    renderError.code = 'GENERATION_FAILED'
    renderError.statusCode = 500
    throw renderError
  }

  // Step 8: LibreOffice post-processing
  const proposalId = `prop_${ulid()}`

  const { docxBuffer: processedDocx, pdfBuffer, warnings } = await postProcess(
    docxBuffer,
    proposalId,
    outputFormat,
    logger
  )

  // Step 9: Upload to storage
  const outputs = {}
  const now = new Date()
  const year = now.getFullYear()
  const month = String(now.getMonth() + 1).padStart(2, '0')
  const expiresAt = new Date(Date.now() + storageProvider.signedUrlExpiry * 1000).toISOString()

  if (processedDocx) {
    const docxKey = `${storageProvider.outputPrefix}/${year}/${month}/${proposalId}.docx`
    await storageProvider.putProposal(docxKey, processedDocx, 'application/vnd.openxmlformats-officedocument.wordprocessingml.document')
    const docxUrl = await storageProvider.getSignedUrl(docxKey)
    outputs.docx = { s3Key: docxKey, downloadUrl: docxUrl, expiresAt }
  }

  if (pdfBuffer) {
    const pdfKey = `${storageProvider.outputPrefix}/${year}/${month}/${proposalId}.pdf`
    await storageProvider.putProposal(pdfKey, pdfBuffer, 'application/pdf')
    const pdfUrl = await storageProvider.getSignedUrl(pdfKey)
    outputs.pdf = { s3Key: pdfKey, downloadUrl: pdfUrl, expiresAt }
  }

  return {
    proposalId,
    proposalNumber: templateData.proposalNumber,
    templateVersion: meta.version || '',
    outputFormat,
    outputs,
    warnings,
  }
}
