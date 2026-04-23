import { test } from 'node:test'
import assert from 'node:assert/strict'
import { buildPremiumSummary } from '../src/utils/premiumSummary.js'

test('two GL quotes → grouped, subtotals correct', () => {
  const quotes = [
    { lineOfBusiness: 'GeneralLiability', carrier: 'Carrier A', premium: 485000, taxes: 12125, fees: 2500, totalCost: 499625 },
    { lineOfBusiness: 'GeneralLiability', carrier: 'Carrier B', premium: 425000, taxes: 0, fees: 0, totalCost: 427500 },
  ]
  const result = buildPremiumSummary(quotes)
  assert.equal(result.byLob.length, 1)
  const gl = result.byLob[0]
  assert.equal(gl.lineOfBusiness, 'GeneralLiability')
  assert.equal(gl.displayName, 'General Liability')
  assert.equal(gl.quotes.length, 2)
  assert.equal(gl.subtotalPremium, '$910,000')
  assert.equal(gl.subtotalTotalCost, '$927,125')
})

test('grand total sums all LOBs', () => {
  const quotes = [
    { lineOfBusiness: 'GeneralLiability', carrier: 'A', premium: 100000, taxes: 5000, fees: 1000, totalCost: 106000 },
    { lineOfBusiness: 'WorkersCompensation', carrier: 'B', premium: 200000, taxes: 0, fees: 0, totalCost: 200000 },
  ]
  const result = buildPremiumSummary(quotes)
  assert.equal(result.grandTotalPremium, '$300,000')
  assert.equal(result.grandTotalTaxes, '$5,000')
  assert.equal(result.grandTotalFees, '$1,000')
  assert.equal(result.grandTotalCost, '$306,000')
})

test('null premium → treated as 0', () => {
  const quotes = [
    { lineOfBusiness: 'Cyber', carrier: 'X', premium: null, taxes: null, fees: null, totalCost: null },
  ]
  const result = buildPremiumSummary(quotes)
  assert.equal(result.grandTotalPremium, '$0')
  assert.equal(result.grandTotalCost, '$0')
})

test('empty quotes → zero totals', () => {
  const result = buildPremiumSummary([])
  assert.equal(result.byLob.length, 0)
  assert.equal(result.grandTotalPremium, '$0')
})
