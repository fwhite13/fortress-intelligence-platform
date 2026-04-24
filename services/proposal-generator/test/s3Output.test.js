import { test, mock } from 'node:test'
import assert from 'node:assert/strict'

let mockPutObjectResult = { $metadata: { httpStatusCode: 200 } }
let mockGetSignedUrlResult = 'https://fortress-tools.s3.amazonaws.com/proposals/2026/04/prop_ABC.docx?X-Amz-Signature=fake'

mock.module('@aws-sdk/client-s3', {
  namedExports: {
    S3Client: class MockS3Client {
      send(command) { return Promise.resolve(mockPutObjectResult) }
    },
    PutObjectCommand: class PutObjectCommand {
      constructor(params) { this.input = params }
    },
    GetObjectCommand: class GetObjectCommand {
      constructor(params) { this.input = params }
    },
  }
})

mock.module('@aws-sdk/s3-request-presigner', {
  namedExports: {
    getSignedUrl: async (client, command, options) => {
      return mockGetSignedUrlResult
    }
  }
})

const { uploadProposal } = await import('../src/services/s3Output.js')

const mockS3Client = {
  send: async (cmd) => mockPutObjectResult
}

test('uploadProposal: returns correct key format', async (t) => {
  const now = new Date()
  const year = now.getFullYear()
  const month = String(now.getMonth() + 1).padStart(2, '0')

  const result = await uploadProposal(
    mockS3Client,
    Buffer.from('fake-docx'),
    'prop_TEST01',
    'docx',
    'application/vnd.openxmlformats-officedocument.wordprocessingml.document'
  )

  assert.equal(result.s3Key, `proposals/${year}/${month}/prop_TEST01.docx`)
  assert.ok(result.downloadUrl.startsWith('https://'), 'downloadUrl should be a URL')
  assert.ok(result.expiresAt, 'expiresAt should be set')

  // Verify expiresAt is ~2 hours in the future
  const expiresAt = new Date(result.expiresAt)
  const diffMs = expiresAt - Date.now()
  assert.ok(diffMs > 0, 'expiresAt should be in the future')
  assert.ok(diffMs <= 2 * 60 * 60 * 1000 + 5000, 'expiresAt should be within 2 hours + buffer')
})

test('uploadProposal: PDF extension works', async (t) => {
  const now = new Date()
  const year = now.getFullYear()
  const month = String(now.getMonth() + 1).padStart(2, '0')

  const result = await uploadProposal(
    mockS3Client,
    Buffer.from('fake-pdf'),
    'prop_TEST02',
    'pdf',
    'application/pdf'
  )

  assert.equal(result.s3Key, `proposals/${year}/${month}/prop_TEST02.pdf`)
})
