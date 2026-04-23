import { test } from 'node:test'
import assert from 'node:assert/strict'
import { formatAttribute } from '../src/utils/formatAttribute.js'

test('currency: 1000000 → $1,000,000', () => {
  assert.equal(formatAttribute('1000000', 'currency'), '$1,000,000')
})

test('currency: 0 → $0', () => {
  assert.equal(formatAttribute('0', 'currency'), '$0')
})

test('currency: number input 5000 → $5,000', () => {
  assert.equal(formatAttribute(5000, 'currency'), '$5,000')
})

test('currency_short: 25000000 → $25M', () => {
  assert.equal(formatAttribute('25000000', 'currency_short'), '$25M')
})

test('currency_short: 1500000 → $1.5M', () => {
  assert.equal(formatAttribute('1500000', 'currency_short'), '$1.5M')
})

test('currency_short: 500000 → $500K', () => {
  assert.equal(formatAttribute('500000', 'currency_short'), '$500K')
})

test('currency_short: 750 → $750', () => {
  assert.equal(formatAttribute('750', 'currency_short'), '$750')
})

test('decimal: 0.88 → 0.88', () => {
  assert.equal(formatAttribute('0.88', 'decimal'), '0.88')
})

test('decimal: 1.00 → 1', () => {
  assert.equal(formatAttribute('1.00', 'decimal'), '1')
})

test('percent: 0.88 → 88%', () => {
  assert.equal(formatAttribute('0.88', 'percent'), '88%')
})

test('percent: 1.5 → 150%', () => {
  assert.equal(formatAttribute('1.5', 'percent'), '150%')
})

test('text: Claims-made → Claims-made', () => {
  assert.equal(formatAttribute('Claims-made', 'text'), 'Claims-made')
})

test('date: 2026-07-01 → 07/01/2026', () => {
  assert.equal(formatAttribute('2026-07-01', 'date'), '07/01/2026')
})

test('null value → ""', () => {
  assert.equal(formatAttribute(null, 'currency'), '')
})

test('undefined value → ""', () => {
  assert.equal(formatAttribute(undefined, 'currency'), '')
})

test('unknown format → pass-through', () => {
  assert.equal(formatAttribute('foo', 'unknown_format'), 'foo')
})

test('no format → pass-through', () => {
  assert.equal(formatAttribute('bar', null), 'bar')
})
