// src/services/documentRenderer.js
import PizZip from 'pizzip'
import Docxtemplater from 'docxtemplater'
import { createRequire } from 'module'
import { ulid } from 'ulid'
import { GetObjectCommand } from '@aws-sdk/client-s3'
import { loadTemplate, LOB_PARTIAL_MAP, TEMPLATE_BUCKET, TEMPLATE_PREFIX } from './templateLoader.js'
import { postProcess } from './postProcessor.js'
import { uploadProposal } from './s3Output.js'
import { renderLobPartial } from './lobRenderer.js'
import { renderBoilerplate } from './boilerplateRenderer.js'
import { assembleTemplateData } from './assembleTemplateData.js'

const require = createRequire(import.meta.url)
const ImageModule = require('docxtemplater-image-module-free')

function generateProposalNumber() {
  const now = new Date()
  const year = now.getFullYear()
  const seq = Math.floor(Math.random() * 99999).toString().padStart(5, '0')
  return `PROP-${year}-${seq}`
}

async function streamToBuffer(stream) {
  const chunks = []
  for await (const chunk of stream) {
    chunks.push(chunk instanceof Buffer ? chunk : Buffer.from(chunk))
  }
  return Buffer.concat(chunks)
}

/**
 * Try to load the vertical logo from S3.
 * Tries .png first, then .svg. Returns null if not found.
 */
async function loadLogo(s3Client, templateId, logger) {
  for (const ext of ['png', 'svg']) {
    const key = `${TEMPLATE_PREFIX}/verticals/${templateId}/logo.${ext}`
    try {
      const response = await s3Client.send(new GetObjectCommand({ Bucket: TEMPLATE_BUCKET, Key: key }))
      return await streamToBuffer(response.Body)
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
 * Main rendering pipeline. Orchestrates template loading, LOB partial rendering,
 * boilerplate rendering, data assembly, and master template rendering.
 *
 * @param {Object} payload - validated request payload
 * @param {S3Client} s3Client - AWS S3 client
 * @param {Object} logger - pino logger
 * @returns {Promise<{ proposalId: string, proposalNumber: string, templateVersion: string, outputFormat: string, outputs: Object, warnings: string[] }>}
 */
export async function renderDocument(payload, s3Client, logger) {
  const templateId = payload.templateId
  const quotes = payload.quotes || []
  const outputFormat = payload.outputFormat || 'docx'

  // Step 1: Load template (meta + master.docx + LOB partials + boilerplate registry)
  const { meta, masterDocx, lobPartials, boilerplateRegistry, selectedBoilerplate } = await loadTemplate(
    templateId,
    quotes,
    payload.templateConfig
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
    s3Client,
    TEMPLATE_BUCKET,
    logger
  )

  // Step 5: Load vertical logo (graceful — null if not found)
  const logoBuffer = await loadLogo(s3Client, templateId, logger)

  // Step 6: Assemble full template data
  payload.proposalNumber = resolvedProposalNumber
  const templateData = assembleTemplateData(payload, meta, logoBuffer, lobSectionsXml, boilerplateSectionsXml, logger)

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

  // Step 9: Upload to S3
  const outputs = {}

  if (processedDocx) {
    outputs.docx = await uploadProposal(
      s3Client,
      processedDocx,
      proposalId,
      'docx',
      'application/vnd.openxmlformats-officedocument.wordprocessingml.document'
    )
  }

  if (pdfBuffer) {
    outputs.pdf = await uploadProposal(
      s3Client,
      pdfBuffer,
      proposalId,
      'pdf',
      'application/pdf'
    )
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
