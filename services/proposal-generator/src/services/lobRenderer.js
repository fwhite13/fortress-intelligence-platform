// src/services/lobRenderer.js
import PizZip from 'pizzip'
import Docxtemplater from 'docxtemplater'
import { formatAttribute, LOB_DISPLAY_NAMES } from '../utils/formatAttribute.js'

/**
 * Build schedule items with formattedValue on attributes, and process children.
 */
function buildScheduleItems(scheduleItems) {
  if (!scheduleItems || scheduleItems.length === 0) return []

  return scheduleItems.map(item => {
    const formattedAttributes = (item.attributes || []).map(a => ({
      ...a,
      formattedValue: formatAttribute(a.value, a.format),
    }))

    const children = (item.children || []).map(child => ({
      itemType: child.itemType || '',
      itemNumber: child.itemNumber || null,
      description: child.description || '',
      address: child.address || null,
      formattedAttributes: (child.attributes || []).map(a => ({
        ...a,
        formattedValue: formatAttribute(a.value, a.format),
      })),
    }))

    return {
      itemType: item.itemType || '',
      itemNumber: item.itemNumber || null,
      description: item.description || '',
      address: item.address || null,
      formattedAttributes,
      children,
    }
  })
}

/**
 * Extract the inner body XML from a rendered .docx buffer.
 * Returns everything between <w:body> and </w:body>, excluding the final <w:sectPr>.
 * @param {Buffer} docxBuffer
 * @returns {string}
 */
function extractBodyXml(docxBuffer) {
  const zip = new PizZip(docxBuffer)
  const documentXml = zip.file('word/document.xml').asText()

  // Extract content between <w:body> and </w:body>
  const bodyMatch = documentXml.match(/<w:body>([\s\S]*?)<\/w:body>/)
  if (!bodyMatch) return ''

  let bodyContent = bodyMatch[1]

  // Strip the final <w:sectPr ...>...</w:sectPr> — it would corrupt master template page layout
  // sectPr can be self-closing or have content, handle both forms
  bodyContent = bodyContent.replace(/<w:sectPr\b[\s\S]*?<\/w:sectPr>\s*$/, '')
  bodyContent = bodyContent.replace(/<w:sectPr\b[^>]*\/>\s*$/, '')

  return bodyContent.trim()
}

/**
 * Render a LOB partial docx with quote data and extract the body XML.
 * @param {Object} quote - quote object from payload
 * @param {Buffer} lobDocxBuffer - the partial .docx buffer
 * @param {Object} logger - pino logger
 * @returns {Promise<string>} raw WordML XML (body content, no sectPr)
 */
export async function renderLobPartial(quote, lobDocxBuffer, logger) {
  const sectionTitle = quote.sectionTitle || LOB_DISPLAY_NAMES[quote.lineOfBusiness] || quote.lineOfBusiness

  const templateData = {
    sectionTitle,
    lineOfBusiness: quote.lineOfBusiness || '',
    ...(() => {
      const c = typeof quote.carrier === 'string'
        ? { name: quote.carrier, amBestRating: '', naic: '' }
        : (quote.carrier || {})
      return {
        'carrier.name': c.name || '',
        'carrier.amBestRating': c.amBestRating || '',
        'carrier.naic': c.naic || null,
      }
    })(),
    quoteNumber: quote.quoteNumber || null,
    policyNumber: quote.policyNumber || null,
    status: quote.status || '',
    isAdmitted: quote.isAdmitted ?? null,
    premium: formatAttribute(quote.premium, 'currency'),
    taxes: formatAttribute(quote.taxes, 'currency'),
    fees: formatAttribute(quote.fees, 'currency'),
    surcharges: formatAttribute(quote.surcharges, 'currency'),
    totalCost: formatAttribute(quote.totalCost, 'currency'),
    attributes: (quote.attributes || []).map(a => ({
      ...a,
      formattedValue: formatAttribute(a.value, a.format),
    })),
    coverages: quote.coverages || [],
    endorsements: quote.endorsements || [],
    deductibles: (quote.deductibles || []).map(d => {
      // Compute a display value: percentage deductibles show as "5%", flat as "$25,000"
      let formattedValue = ''
      if (d.percentage != null) {
        formattedValue = `${d.percentage}%`
      } else if (d.amount != null && d.amount !== 0) {
        formattedValue = formatAttribute(String(d.amount), 'currency')
      } else if (d.description) {
        formattedValue = d.description
      }
      return { ...d, formattedValue }
    }),
    coverageParts: quote.coverageParts || [],
    scheduleItems: buildScheduleItems(quote.scheduleItems || []),
    notes: quote.notes || null,
  }

  const zip = new PizZip(lobDocxBuffer)
  const doc = new Docxtemplater(zip, {
    paragraphLoop: true,
    linebreaks: true,
    modules: [],
    nullGetter(part) {
      return part.module ? null : ''
    },
  })

  doc.render(templateData)
  const rendered = doc.getZip().generate({ type: 'nodebuffer' })

  return extractBodyXml(rendered)
}
