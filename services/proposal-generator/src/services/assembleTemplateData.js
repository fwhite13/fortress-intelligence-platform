// src/services/assembleTemplateData.js
import { formatDate, formatAttribute, LOB_DISPLAY_NAMES } from '../utils/formatAttribute.js'
import { buildPremiumSummary } from '../utils/premiumSummary.js'
import { buildMarketResponseData } from '../utils/marketResponse.js'

function formatAddress(address) {
  if (!address) return ''
  const parts = [address.street1, address.city, address.state, address.zip].filter(Boolean)
  // Format: "123 Main St, Atlanta, GA 30301"
  if (parts.length >= 3) {
    const [street, city, state, zip] = [address.street1, address.city, address.state, address.zip]
    return [street, `${city}, ${state} ${zip}`.trim()].filter(Boolean).join(', ')
  }
  return parts.join(', ')
}

function generateProposalNumber() {
  const now = new Date()
  const year = now.getFullYear()
  const seq = Math.floor(Math.random() * 99999).toString().padStart(5, '0')
  return `PROP-${year}-${seq}`
}

/**
 * Assemble the full template data object for docxtemplater.
 */
export function assembleTemplateData(payload, templateMeta, logoBuffer, lobSectionsXml, boilerplateSectionsXml, logger) {
  const insured = payload.insured || {}
  const address = insured.address || {}
  const contact = insured.primaryContact || {}
  const period = payload.policyPeriod || {}
  const metadata = payload.metadata || {}

  const effectiveDate = formatDate(period.effectiveDate || '')
  const expirationDate = formatDate(period.expirationDate || '')

  return {
    // Flat insured fields
    insuredName: insured.name || '',
    insuredDba: insured.dba || null,
    insuredEntityType: insured.entityType || null,
    insuredFein: insured.fein || null,
    insuredAddressStreet1: address.street1 || '',
    insuredAddressCity: address.city || '',
    insuredAddressState: address.state || '',
    insuredAddressZip: address.zip || '',
    insuredAddressFull: formatAddress(address),
    insuredContactName: contact.name || null,
    insuredContactTitle: contact.title || null,
    insuredContactEmail: contact.email || null,
    insuredContactPhone: contact.phone || null,

    // Policy period
    effectiveDate,
    expirationDate,
    policyPeriodDisplay: effectiveDate && expirationDate ? `${effectiveDate} \u2013 ${expirationDate}` : '',

    // Metadata
    amName: metadata.amName || '',
    amEmail: metadata.amEmail || '',
    proposalNumber: payload.proposalNumber || generateProposalNumber(),
    generatedDate: new Date().toLocaleDateString('en-US', { month: '2-digit', day: '2-digit', year: 'numeric' }),
    templateVersion: templateMeta?.version || '',

    // Narratives — flat keys required for docxtemplater literal key lookup
    'narratives.executive_summary': payload.narratives?.executive_summary || '',
    'narratives.recommendations': payload.narratives?.recommendations || '',
    'narratives.special_notes': payload.narratives?.special_notes || null,

    // Team
    team: payload.team || [],
    hasTeam: (payload.team || []).length > 0,

    // Premium summary — new flat table shape
    ...(() => {
      const ps = buildPremiumSummary(payload.quotes || [])
      return {
        premiumRows: ps.premiumRows,
        grandTotal: ps.grandTotal,
      }
    })(),

    // Market responses
    marketResponses: buildMarketResponseData(payload.marketResponses),
    hasMarketResponses: (payload.marketResponses || []).length > 0,

    // Bill payment
    billPaymentOptions: payload.billPaymentOptions || null,
    hasBillPaymentOptions: !!payload.billPaymentOptions,

    // Injected XML sections
    lobSectionsXml: lobSectionsXml || '',
    boilerplateSectionsXml: boilerplateSectionsXml || '',

    // Logo (base64 for image module)
    verticalLogoBase64: logoBuffer ? logoBuffer.toString('base64') : null,
    hasLogo: !!logoBuffer,
  }
}

function formatCurrencyWc(value) {
  if (value == null || value === '') return ''
  const num = typeof value === 'number' ? value : parseFloat(value)
  if (isNaN(num)) return String(value)
  return '$' + num.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
}

/**
 * Assemble template data for the NBAIS Workers' Compensation proposal.
 * This vertical uses a fixed-format template with WC-specific computed fields.
 *
 * Field mapping:
 *   memberName         ← insured.name
 *   memberAddress      ← insured.address (formatted)
 *   memberLegalName    ← nbaisWc.memberLegalName || insured.name
 *   policyPeriod       ← policyPeriod.effectiveDate – expirationDate (formatted)
 *   quoteDate          ← nbaisWc.quoteDate || today
 *   estPremium         ← quotes[0].premium (WorkersCompensation) — formatted currency
 *   surplusContribution← COMPUTED: basePremium * 0.08 — formatted currency
 *   employersLiabilityFee ← CONSTANT: $120 — formatted currency
 *   totalEstimatedPremium ← COMPUTED: estPremium + surplusContribution + elFee — formatted
 *   downPayment        ← COMPUTED: totalEstimatedPremium * 0.25 — formatted currency
 *   classSchedule[]    ← quotes[0].scheduleItems[itemType=employee_class]
 *   excludedPersons[]  ← payload.nbaisWc.excludedPersons[]
 */
export function assembleNbaisWcTemplateData(payload, templateMeta, logos, logger) {
  const insured = payload.insured || {}
  const address = insured.address || {}
  const period = payload.policyPeriod || {}

  const effectiveDate = formatDate(period.effectiveDate || '')
  const expirationDate = formatDate(period.expirationDate || '')
  const policyPeriodStr = effectiveDate && expirationDate ? `${effectiveDate} \u2013 ${expirationDate}` : ''

  // Extract the WC quote (first WorkersCompensation quote)
  const wcQuote = (payload.quotes || []).find(q => q.lineOfBusiness === 'WorkersCompensation') || {}
  const attrs = Object.fromEntries((wcQuote.attributes || []).map(a => [a.key, a.value]))

  // Build class schedule from employee_class schedule items
  const classSchedule = (wcQuote.scheduleItems || [])
    .filter(i => i.itemType === 'employee_class')
    .map(item => {
      const ia = Object.fromEntries((item.attributes || []).map(a => [a.key, a.value]))
      return {
        state: ia.state || 'NV',
        classCode: ia.class_code || '',
        classDescription: ia.class_description || item.description || '',
        estAnnualPayroll: formatCurrencyWc(ia.payroll ?? ia.estimated_annual_payroll),
        rate: ia.rate_per_hundred != null ? `$${ia.rate_per_hundred}` : (ia.rate || ''),
        classEstPremium: formatCurrencyWc(ia.estimated_premium),
      }
    })

  // Build excluded persons from payload extension field
  const excludedPersons = (payload.nbaisWc?.excludedPersons || []).map(ep => ({
    name: typeof ep === 'string' ? ep : (ep.name || ''),
  }))

  // Compute premium fields
  const basePremiumNum = wcQuote.premium != null
    ? Number(wcQuote.premium)
    : (attrs.estimated_premium ? Number(attrs.estimated_premium) : 0)

  const surplusContributionNum = Math.round(basePremiumNum * 0.08 * 100) / 100
  const elFeeNum = 120   // BAWNSIG program constant — $120 EL fee
  const totalEstimatedPremiumNum = basePremiumNum + surplusContributionNum + elFeeNum
  const downPaymentNum = Math.round(totalEstimatedPremiumNum * 0.25 * 100) / 100

  // Quote date
  const quoteDate = payload.nbaisWc?.quoteDate
    ? formatDate(payload.nbaisWc.quoteDate)
    : new Date().toLocaleDateString('en-US', { month: '2-digit', day: '2-digit', year: 'numeric' })

  return {
    // Member identity
    memberName: insured.name || '',
    memberAddress: formatAddress(address),
    memberLegalName: payload.nbaisWc?.memberLegalName || insured.name || '',

    // Policy period — both key names for template compatibility
    policyPeriod: policyPeriodStr,
    policyPeriodDisplay: policyPeriodStr,
    quoteDate,

    // Premium fields (all formatted currency strings)
    basePremium: formatCurrencyWc(basePremiumNum),
    estPremium: formatCurrencyWc(basePremiumNum),
    surplusContribution: formatCurrencyWc(surplusContributionNum),
    employersLiabilityFee: formatCurrencyWc(elFeeNum),
    totalEstimatedPremium: formatCurrencyWc(totalEstimatedPremiumNum),
    downPayment: formatCurrencyWc(downPaymentNum),

    // Class schedule (loop data)
    classSchedule,

    // Excluded persons (conditional + inner loop)
    hasExcludedPersons: excludedPersons.length > 0,
    excludedPersons,

    // Logos (base64 for image module)
    stackedLogoBase64: logos?.stacked ? logos.stacked.toString('base64') : null,
    horizontalLogoBase64: logos?.horizontal ? logos.horizontal.toString('base64') : null,

    // Standard compatibility fields
    proposalNumber: payload.proposalNumber || generateProposalNumber(),
    generatedDate: new Date().toLocaleDateString('en-US', { month: '2-digit', day: '2-digit', year: 'numeric' }),
    templateVersion: templateMeta?.version || '',
  }
}
