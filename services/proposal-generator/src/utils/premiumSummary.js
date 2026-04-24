// src/utils/premiumSummary.js
import { formatAttribute, LOB_DISPLAY_NAMES } from './formatAttribute.js'

/**
 * Format a numeric value as USD currency: $1,234,567
 */
function formatCurrency(value) {
  if (value == null || value === '') return '$0'
  const num = Number(value)
  if (isNaN(num)) return '$0'
  return '$' + num.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
}

/**
 * Get exposure highlights string for a quote, based on LOB.
 */
function getExposureHighlights(quote) {
  const lob = quote.lineOfBusiness
  const attrs = Object.fromEntries((quote.attributes || []).map(a => [a.key, a.value]))

  if (lob === 'GeneralLiability') {
    const occ = attrs.each_occurrence ? formatCurrency(attrs.each_occurrence) : ''
    const agg = attrs.general_aggregate ? formatCurrency(attrs.general_aggregate) : ''
    if (occ && agg) return `${occ} Each Occurrence / ${agg} General Aggregate`
    if (occ) return `${occ} Each Occurrence`
    return ''
  }

  if (lob === 'WorkersCompensation') {
    return 'Statutory'
  }

  if (lob === 'CommercialProperty') {
    const tiv = attrs.total_insured_value || attrs.building_limit
    return tiv ? `${formatCurrency(tiv)} TIV` : ''
  }

  // Default: first attribute value
  if (quote.attributes && quote.attributes.length > 0) {
    return quote.attributes[0].value || ''
  }
  return ''
}

/**
 * Build the unified flat premium summary table.
 * Returns: { premiumRows: [...], grandTotal: '$X' }
 */
export function buildPremiumSummary(quotes) {
  if (!quotes || quotes.length === 0) {
    return { premiumRows: [], grandTotal: '$0.00' }
  }

  let grandTotal = 0

  const premiumRows = quotes.map(quote => {
    const premium = Number(quote.premium) || 0
    grandTotal += premium

    const displayName = LOB_DISPLAY_NAMES[quote.lineOfBusiness] || quote.lineOfBusiness || ''
    const exposureHighlights = getExposureHighlights(quote)

    return {
      coverageLabel: displayName,
      exposureHighlights,
      formattedPremium: formatCurrency(premium),
    }
  })

  return {
    premiumRows,
    grandTotal: formatCurrency(grandTotal),
  }
}
