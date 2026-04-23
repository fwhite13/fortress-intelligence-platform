import { test } from 'node:test'
import assert from 'node:assert/strict'
import { buildMarketResponseData } from '../src/utils/marketResponse.js'

test('status enum → display labels', () => {
  const input = [
    { carrierName: 'GuideOne', status: 'declined', reason: 'Outside appetite', lineOfBusiness: 'GeneralLiability' },
    { carrierName: 'Wesco', status: 'no_response', lineOfBusiness: 'GeneralLiability' },
    { carrierName: 'Other Co', status: 'not_competitive', lineOfBusiness: 'Cyber' },
    { carrierName: 'Acme', status: 'indication', lineOfBusiness: 'WorkersCompensation' },
    { carrierName: 'Best', status: 'quoted', lineOfBusiness: 'CommercialAuto' },
  ]
  const result = buildMarketResponseData(input)
  assert.equal(result.length, 5)
  assert.equal(result[0].statusDisplay, 'Declined')
  assert.equal(result[1].statusDisplay, 'No Response')
  assert.equal(result[2].statusDisplay, 'Not Competitive')
  assert.equal(result[3].statusDisplay, 'Indication')
  assert.equal(result[4].statusDisplay, 'Quoted')
})

test('null input → []', () => {
  assert.deepEqual(buildMarketResponseData(null), [])
})

test('undefined input → []', () => {
  assert.deepEqual(buildMarketResponseData(undefined), [])
})

test('lobDisplay populated from LOB_DISPLAY_NAMES', () => {
  const result = buildMarketResponseData([
    { carrierName: 'A', status: 'declined', lineOfBusiness: 'GeneralLiability' }
  ])
  assert.equal(result[0].lobDisplay, 'General Liability')
})
