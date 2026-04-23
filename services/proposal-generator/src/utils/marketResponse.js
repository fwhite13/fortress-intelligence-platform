// src/utils/marketResponse.js
import { LOB_DISPLAY_NAMES } from './formatAttribute.js'

const STATUS_DISPLAY = {
  quoted: 'Quoted',
  declined: 'Declined',
  no_response: 'No Response',
  not_competitive: 'Not Competitive',
  indication: 'Indication',
}

/**
 * Build market response display data.
 * @param {Array|null|undefined} marketResponses
 * @returns {Array<{carrierName, status, statusDisplay, reason, lineOfBusiness, lobDisplay}>}
 */
export function buildMarketResponseData(marketResponses) {
  if (!marketResponses || marketResponses.length === 0) return []

  return marketResponses.map(mr => ({
    carrierName: mr.carrierName || '',
    status: mr.status || '',
    statusDisplay: STATUS_DISPLAY[mr.status] || (mr.status || ''),
    reason: mr.reason || null,
    lineOfBusiness: mr.lineOfBusiness || '',
    lobDisplay: LOB_DISPLAY_NAMES[mr.lineOfBusiness] || mr.lineOfBusiness || '',
  }))
}
