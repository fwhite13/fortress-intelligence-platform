// src/services/lobRenderer.js
import PizZip from 'pizzip'
import Docxtemplater from 'docxtemplater'
import { formatAttribute, formatDate, LOB_DISPLAY_NAMES } from '../utils/formatAttribute.js'

/**
 * Format a numeric string as USD currency: $1,234,567
 * Returns empty string for null/undefined/empty.
 */
function formatCurrency(value) {
  if (value == null || value === '') return ''
  const num = parseFloat(value)
  if (isNaN(num)) return value
  return '$' + num.toLocaleString('en-US', { minimumFractionDigits: 0, maximumFractionDigits: 0 })
}

/**
 * Format enum-style string values for display.
 * "replacement_cost" -> "Replacement Cost"
 * "special" -> "Special"
 */
function formatEnumValue(val) {
  if (!val || typeof val !== 'string') return val
  return val.replace(/_/g, ' ').replace(/\b\w/g, c => c.toUpperCase())
}

/**
 * Flatten location-type schedule items into {itemNumber, streetAddress, city, state, zip}.
 */
function buildScheduleLocations(scheduleItems) {
  if (!scheduleItems || scheduleItems.length === 0) return []
  return scheduleItems
    .filter(i => i.itemType === 'location')
    .map(item => {
      const attrs = Object.fromEntries((item.attributes || []).map(a => [a.key, a.value]))
      return {
        itemNumber: item.itemNumber || '',
        streetAddress: attrs.address_street1 || '',
        city: attrs.address_city || '',
        state: attrs.address_state || '',
        zip: attrs.address_zip || '',
      }
    })
}

/**
 * Flatten GL classification items into flat keys for docxtemplater.
 * GL classifications appear as nested scheduleItems UNDER each location item.
 */
function buildGlClassifications(scheduleItems) {
  if (!scheduleItems || scheduleItems.length === 0) return []
  const results = []
  for (const item of scheduleItems) {
    // Top-level gl_classification items
    if (item.itemType === 'gl_classification') {
      const attrs = Object.fromEntries((item.attributes || []).map(a => [a.key, a.value]))
      results.push({
        classCode: attrs.class_code || '',
        classDescription: attrs.class_description || item.description || '',
        exposure: attrs.estimated_exposure != null ? String(attrs.estimated_exposure) : (attrs.exposure || ''),
        exposureBasis: attrs.premium_basis || attrs.exposure_basis || '',
        rate: attrs.rate ? `$${attrs.rate}` : '',
        glPremium: formatCurrency(attrs.estimated_premium || attrs.premium),
      })
    }
    // Nested gl_classification items under location.scheduleItems
    if (item.itemType === 'location' && item.scheduleItems) {
      for (const nested of item.scheduleItems) {
        if (nested.itemType === 'gl_classification') {
          const attrs = Object.fromEntries((nested.attributes || []).map(a => [a.key, a.value]))
          results.push({
            classCode: attrs.class_code || '',
            classDescription: attrs.class_description || nested.description || '',
            exposure: attrs.estimated_exposure != null ? String(attrs.estimated_exposure) : (attrs.exposure || ''),
            exposureBasis: attrs.premium_basis || attrs.exposure_basis || '',
            rate: attrs.rate ? `$${attrs.rate}` : '',
            glPremium: formatCurrency(attrs.estimated_premium || attrs.premium),
          })
        }
      }
    }
  }
  return results
}

/**
 * Flatten WC employee_class schedule items into flat keys for docxtemplater.
 */
function buildWcEmployeeClasses(scheduleItems) {
  if (!scheduleItems || scheduleItems.length === 0) return []
  return scheduleItems
    .filter(i => i.itemType === 'employee_class')
    .map(item => {
      const attrs = Object.fromEntries((item.attributes || []).map(a => [a.key, a.value]))
      return {
        state: attrs.state || '',
        classCode: attrs.class_code || '',
        classDescription: attrs.class_description || item.description || '',
        payroll: formatCurrency(attrs.payroll),
        ratePerHundred: attrs.rate_per_hundred ? `$${attrs.rate_per_hundred}` : '',
        estimatedPremium: formatCurrency(attrs.estimated_premium),
      }
    })
}

/**
 * Build property schedule rows: each building under each location becomes one flat row.
 * Handles nested scheduleItems: location -> building children.
 */
function buildPropertySchedule(scheduleItems = []) {
  const rows = []
  let locNum = 0
  for (const item of scheduleItems) {
    if (item.itemType === 'location') {
      locNum++
      const locAttrs = Object.fromEntries((item.attributes || []).map(a => [a.key, a.value]))
      const buildings = (item.scheduleItems || []).filter(b => b.itemType === 'building')
      buildings.forEach((bld, bldIdx) => {
        const b = Object.fromEntries((bld.attributes || []).map(a => [a.key, a.value]))
        rows.push({
          locationNumber: String(locNum),
          buildingNumber: String(bldIdx + 1),
          description: b.building_description || b.description || '',
          address: [locAttrs.address_street1, locAttrs.address_city, locAttrs.address_state].filter(Boolean).join(', '),
          buildingLimit: b.building_limit ? formatCurrency(parseFloat(b.building_limit)) : '',
        })
      })
    }
  }
  return rows
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
      formattedValue: a.format === 'text'
        ? formatEnumValue(formatAttribute(a.value, a.format))
        : formatAttribute(a.value, a.format),
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
    scheduleLocations: buildScheduleLocations(quote.scheduleItems || []),
    glClassifications: buildGlClassifications(quote.scheduleItems || []),
    wcEmployeeClasses: buildWcEmployeeClasses(quote.scheduleItems || []),
    propertySchedule: buildPropertySchedule(quote.scheduleItems || []),
    namedInsureds: (quote.additionalNamedInsureds || []).map(n => ({ name: typeof n === 'string' ? n : (n.name || '') })),
    effectiveDate: formatDate((quote.policyPeriod || {}).effectiveDate || ''),
    expirationDate: formatDate((quote.policyPeriod || {}).expirationDate || ''),
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
