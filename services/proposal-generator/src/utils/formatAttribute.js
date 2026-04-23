// src/utils/formatAttribute.js
// Formats attribute values per Appendix B of spec

export const LOB_DISPLAY_NAMES = {
  GeneralLiability: 'General Liability',
  WorkersCompensation: 'Workers Compensation',
  CommercialProperty: 'Commercial Property',
  CommercialAuto: 'Commercial Auto',
  InlandMarine: 'Inland Marine',
  Umbrella: 'Commercial Umbrella',
  Excess: 'Excess Liability',
  Cyber: 'Cyber Liability',
  DirectorsOfficers: 'Directors & Officers',
  EmploymentPractices: 'Employment Practices Liability',
  ManagementLiability: 'Management Liability',
  ProfessionalLiability: 'Professional Liability',
  Crime: 'Crime / Fidelity',
  ForeignPackage: 'Foreign Package',
  KidnapRansom: 'Kidnap & Ransom',
  ParticipantAccident: 'Participant Accident',
  ActiveAssailant: 'Active Assailant',
  Pollution: 'Pollution Liability',
  BuildersRisk: 'Builders Risk',
  Other: 'Other',
}

/**
 * Format an attribute value according to the given format type.
 * @param {string|number|null|undefined} value
 * @param {string|null|undefined} format - 'currency' | 'currency_short' | 'decimal' | 'percent' | 'text' | 'date'
 * @returns {string}
 */
export function formatAttribute(value, format) {
  // null/undefined value → always return ""
  if (value === null || value === undefined) return ''

  const str = String(value)

  switch (format) {
    case 'currency': {
      // "$N,NNN,NNN" — dollar sign + comma-separated integer
      const num = parseFloat(str)
      if (isNaN(num)) return str
      return '$' + Math.round(num).toLocaleString('en-US')
    }
    case 'currency_short': {
      // >= 1,000,000 → $NM (1 decimal if not whole); >= 1,000 → $NK; else raw dollar
      const num = parseFloat(str)
      if (isNaN(num)) return str
      if (num >= 1_000_000) {
        const m = num / 1_000_000
        // Trim trailing .0: 25.0 → "25M", 1.5 → "1.5M"
        const formatted = m % 1 === 0 ? String(Math.round(m)) : m.toFixed(1).replace(/\.?0+$/, '')
        return '$' + formatted + 'M'
      }
      if (num >= 1_000) {
        const k = num / 1_000
        const formatted = k % 1 === 0 ? String(Math.round(k)) : k.toFixed(1).replace(/\.?0+$/, '')
        return '$' + formatted + 'K'
      }
      return '$' + Math.round(num).toLocaleString('en-US')
    }
    case 'decimal': {
      // Up to 2 decimal places, trim trailing zeros
      const num = parseFloat(str)
      if (isNaN(num)) return str
      // Format with up to 2 decimals, then trim trailing zeros
      return parseFloat(num.toFixed(2)).toString()
    }
    case 'percent': {
      // multiply by 100 + "%"
      const num = parseFloat(str)
      if (isNaN(num)) return str
      const pct = num * 100
      return parseFloat(pct.toFixed(2)).toString() + '%'
    }
    case 'date': {
      // ISO 8601 date string → MM/DD/YYYY
      // Input: "2026-07-01" or "2026-07-01T00:00:00Z"
      const match = str.match(/^(\d{4})-(\d{2})-(\d{2})/)
      if (!match) return str
      const [, year, month, day] = match
      return `${month}/${day}/${year}`
    }
    case 'text':
    default:
      // Pass through as-is
      return str
  }
}

/**
 * Format a date string as MM/DD/YYYY. Convenience wrapper.
 */
export function formatDate(dateStr) {
  return formatAttribute(dateStr, 'date')
}
