import { test, mock, beforeEach } from 'node:test'
import assert from 'node:assert/strict'

let mockSendFn = async () => { throw new Error('mockSendFn not configured') }

mock.module('@aws-sdk/client-s3', {
  namedExports: {
    S3Client: class MockS3Client {
      send(command) { return mockSendFn(command) }
    },
    GetObjectCommand: class GetObjectCommand {
      constructor(params) { this.params = params }
    },
    ListObjectsV2Command: class ListObjectsV2Command {
      constructor(params) { this.params = params }
    },
  },
})

const { loadTemplate, clearCache, LOB_PARTIAL_MAP } = await import('../src/services/templateLoader.js')

function fakeBody(content) {
  const buf = typeof content === 'string' ? Buffer.from(content) : content
  return {
    [Symbol.asyncIterator]: async function* () { yield buf },
  }
}

function makeMetaResponse(meta = {}) {
  return {
    Body: fakeBody(JSON.stringify({ active: true, version: '1.2.0', defaultBoilerplate: [], ...meta })),
  }
}

function makeBufferResponse(content = 'fake-docx-content') {
  return { Body: fakeBody(content) }
}

test('TL1: correct S3 keys for templateId + quotes', async (t) => {
  clearCache()
  const calledKeys = []

  mockSendFn = async (command) => {
    const key = command.params.Key
    calledKeys.push(key)
    if (key === 'verticals/nba-v1/meta.json') return makeMetaResponse()
    if (key === 'verticals/nba-v1/master.docx') return makeBufferResponse()
    if (key === 'lob-partials/general-liability.docx') return makeBufferResponse()
    if (key === 'lob-partials/workers-compensation.docx') return makeBufferResponse()
    if (key === 'registry/boilerplate.json') return makeMetaResponse({ active: true, blocks: {} })
    throw new Error(`Unexpected key: ${key}`)
  }

  const result = await loadTemplate('nba-v1', [
    { lineOfBusiness: 'GeneralLiability' },
    { lineOfBusiness: 'WorkersCompensation' },
  ], null)

  assert.ok(calledKeys.includes('verticals/nba-v1/meta.json'))
  assert.ok(calledKeys.includes('verticals/nba-v1/master.docx'))
  assert.ok(calledKeys.includes('lob-partials/general-liability.docx'))
  assert.ok(calledKeys.includes('lob-partials/workers-compensation.docx'))
  assert.ok(calledKeys.includes('registry/boilerplate.json'))
  assert.equal(result.lobPartials.size, 2)
  assert.ok(result.lobPartials.has('GeneralLiability'))
  assert.ok(result.lobPartials.has('WorkersCompensation'))
})

test('TL2: cache hit on second call — fewer S3 fetches', async (t) => {
  clearCache()
  let sendCount = 0

  mockSendFn = async (command) => {
    sendCount++
    const key = command.params.Key
    if (key === 'verticals/nba-v1/meta.json') return makeMetaResponse()
    if (key === 'verticals/nba-v1/master.docx') return makeBufferResponse()
    if (key === 'registry/boilerplate.json') {
      const noSuchKey = new Error('NoSuchKey')
      noSuchKey.name = 'NoSuchKey'
      throw noSuchKey
    }
    throw new Error(`Unexpected key: ${key}`)
  }

  await loadTemplate('nba-v1', [], null)
  const firstCallCount = sendCount

  await loadTemplate('nba-v1', [], null)
  const secondCallCount = sendCount - firstCallCount

  assert.ok(secondCallCount < firstCallCount, `Second call (${secondCallCount}) should make fewer S3 requests than first (${firstCallCount})`)
})

test('TL3: ForeignPackage → warning logged, no error', async (t) => {
  clearCache()

  mockSendFn = async (command) => {
    const key = command.params.Key
    if (key === 'verticals/nba-v1/meta.json') return makeMetaResponse()
    if (key === 'verticals/nba-v1/master.docx') return makeBufferResponse()
    if (key === 'registry/boilerplate.json') {
      const noSuchKey = new Error('NoSuchKey')
      noSuchKey.name = 'NoSuchKey'
      throw noSuchKey
    }
    throw new Error(`Unexpected key: ${key}`)
  }

  const result = await loadTemplate('nba-v1', [{ lineOfBusiness: 'ForeignPackage' }], null)
  assert.ok(!result.lobPartials.has('ForeignPackage'))
})

test('TL4: NoSuchKey for LOB partial → throws LOB_PARTIAL_MISSING', async (t) => {
  clearCache()

  mockSendFn = async (command) => {
    const key = command.params.Key
    if (key === 'verticals/nba-v1/meta.json') return makeMetaResponse()
    if (key === 'verticals/nba-v1/master.docx') return makeBufferResponse()
    if (key === 'lob-partials/cyber.docx') {
      const noSuchKey = new Error('NoSuchKey')
      noSuchKey.name = 'NoSuchKey'
      noSuchKey.Code = 'NoSuchKey'
      throw noSuchKey
    }
    if (key === 'registry/boilerplate.json') {
      const noSuchKey = new Error('NoSuchKey')
      noSuchKey.name = 'NoSuchKey'
      throw noSuchKey
    }
    throw new Error(`Unexpected key: ${key}`)
  }

  await assert.rejects(
    () => loadTemplate('nba-v1', [{ lineOfBusiness: 'Cyber' }], null),
    (err) => {
      assert.equal(err.code, 'LOB_PARTIAL_MISSING')
      return true
    }
  )
})

test('TL5: active:false template → throws TEMPLATE_NOT_FOUND', async (t) => {
  clearCache()
  mockSendFn = async (command) => {
    if (command.params.Key === 'verticals/nba-v1/meta.json')
      return makeMetaResponse({ active: false })
    throw new Error('Unexpected S3 command')
  }
  await assert.rejects(
    () => loadTemplate('nba-v1', [], null),
    (err) => { assert.equal(err.code, 'TEMPLATE_NOT_FOUND'); return true }
  )
})

test('TL6: missing meta.json → throws TEMPLATE_NOT_FOUND', async (t) => {
  clearCache()
  mockSendFn = async (command) => {
    if (command.params.Key === 'verticals/missing-v1/meta.json') {
      const e = new Error('NoSuchKey'); e.name = 'NoSuchKey'; throw e
    }
    throw new Error('Unexpected S3 command')
  }
  await assert.rejects(
    () => loadTemplate('missing-v1', [], null),
    (err) => { assert.equal(err.code, 'TEMPLATE_NOT_FOUND'); return true }
  )
})
