// src/utils/premiumSummary.js
import { formatAttribute, LOB_DISPLAY_NAMES } from './formatAttribute.js'

/**
 * Aggregate premium data across all quotes.
 * Groups quotes by lineOfBusiness, computes subtotals and grand totals.
 * All monetary values returned as currency-formatted strings.
 */
export function buildPremiumSummary(quotes) {
  if (!quotes || quotes.length === 0) {
    return { byLob: [], grandTotalPremium: '$0', grandTotalTaxes: '$0', grandTotalFees: '$0', grandTotalCost: '$0' }
  }

  // Group by lineOfBusiness, preserving order of first appearance
  const lobOrder = []
  const lobGroups = new Map()

  for (const quote of quotes) {
    const lob = quote.lineOfBusiness
    if (!lobGroups.has(lob)) {
      lobOrder.push(lob)
      lobGroups.set(lob, [])
    }
    lobGroups.get(lob).push(quote)
  }

  let grandTotalPremium = 0
  let grandTotalTaxes = 0
  let grandTotalFees = 0
  let grandTotalCost = 0

  const byLob = lobOrder.map(lob => {
    const lobQuotes = lobGroups.get(lob)
    let subtotalPremium = 0
    let subtotalCost = 0

    const formattedQuotes = lobQuotes.map(q => {
      const premium = Number(q.premium) || 0
      const taxes = Number(q.taxes) || 0
      const fees = Number(q.fees) || 0
      const totalCost = Number(q.totalCost) || 0

      subtotalPremium += premium
      subtotalCost += totalCost
      grandTotalPremium += premium
      grandTotalTaxes += taxes
      grandTotalFees += fees
      grandTotalCost += totalCost

      return {
        carrier: q.carrier || '',
        premium: formatAttribute(premium, 'currency'),
        taxes: formatAttribute(taxes, 'currency'),
        fees: formatAttribute(fees, 'currency'),
        totalCost: formatAttribute(totalCost, 'currency'),
      }
    })

    return {
      lineOfBusiness: lob,
      displayName: LOB_DISPLAY_NAMES[lob] || lob,
      quotes: formattedQuotes,
      subtotalPremium: formatAttribute(subtotalPremium, 'currency'),
      subtotalTotalCost: formatAttribute(subtotalCost, 'currency'),
    }
  })

  return {
    byLob,
    grandTotalPremium: formatAttribute(grandTotalPremium, 'currency'),
    grandTotalTaxes: formatAttribute(grandTotalTaxes, 'currency'),
    grandTotalFees: formatAttribute(grandTotalFees, 'currency'),
    grandTotalCost: formatAttribute(grandTotalCost, 'currency'),
  }
}
