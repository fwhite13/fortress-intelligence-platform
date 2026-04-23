import { test, mock } from 'node:test'
import assert from 'node:assert/strict'
import PizZip from 'pizzip'

// ─── Build a minimal valid .docx buffer with {insuredName} in body ───────────
function buildMinimalDocx(bodyContent) {
  const zip = new PizZip()

  zip.file('[Content_Types].xml', `<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
  <Default Extension="xml" ContentType="application/xml"/>
  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
</Types>`)

  zip.file('_rels/.rels', `<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
</Relationships>`)

  zip.file('word/_rels/document.xml.rels', `<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
</Relationships>`)

  zip.file('word/document.xml', `<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:document xmlns:wpc="http://schemas.microsoft.com/office/word/2010/wordprocessingCanvas"
  xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
  xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
  <w:body>
    ${bodyContent}
  </w:body>
</w:document>`)

  return zip.generate({ type: 'nodebuffer' })
}

// Mock @aws-sdk/client-s3 BEFORE importing documentRenderer
mock.module('@aws-sdk/client-s3', {
  namedExports: {
    S3Client: class MockS3Client {
      constructor() {}
      send(command) {
        return mockS3Send(command)
      }
    },
    GetObjectCommand: class GetObjectCommand {
      constructor(params) { this.params = params }
    },
    ListObjectsV2Command: class ListObjectsV2Command {
      constructor(params) { this.params = params }
    },
  },
})

let mockS3Send = async (command) => { throw new Error('mockS3Send not configured') }

// We also need to mock templateLoader since it creates its own S3Client
mock.module('../src/services/templateLoader.js', {
  namedExports: {
    LOB_PARTIAL_MAP: new Map([
      ['GeneralLiability', 'general-liability.docx'],
      ['ForeignPackage', null],
    ]),
    LOB_DISPLAY_NAMES: {
      GeneralLiability: 'General Liability',
    },
    clearCache: () => {},
    loadTemplate: async (templateId, quotes, templateConfig) => {
      return {
        meta: { version: '1.0.0', active: true, defaultBoilerplate: [] },
        masterDocx: buildMinimalDocx('<w:p><w:r><w:t>{insuredName}</w:t></w:r></w:p>'),
        lobPartials: new Map(),
        boilerplateRegistry: { blocks: {} },
        selectedBoilerplate: [],
      }
    },
  },
})

// Now import documentRenderer
const { renderDocument } = await import('../src/services/documentRenderer.js')

const minimalPayload = {
  templateId: 'test-v1',
  insured: {
    name: 'Acme Church',
    address: { street1: '123 Main St', city: 'Tyler', state: 'TX', zip: '75701' },
  },
  policyPeriod: { effectiveDate: '2026-07-01', expirationDate: '2027-07-01' },
  metadata: { amName: 'Sarah Nguyen', amEmail: 'snguyen@test.com' },
  quotes: [],
  marketResponses: null,
}

test('renderDocument returns a Buffer', async (t) => {
  mockS3Send = async (command) => {
    // No S3 calls expected since templateLoader is mocked and no logo found
    // But logo lookup will try → simulate not found
    const key = command.params?.Key || ''
    if (key.includes('logo')) {
      const err = new Error('NoSuchKey')
      err.name = 'NoSuchKey'
      throw err
    }
    throw new Error(`Unexpected S3 key: ${key}`)
  }

  const mockLogger = { info: () => {}, warn: () => {}, debug: () => {}, error: () => {} }
  const mockS3Client = { send: (cmd) => mockS3Send(cmd) }

  const result = await renderDocument(minimalPayload, mockS3Client, mockLogger)
  assert.ok(result.docxBuffer instanceof Buffer, 'docxBuffer should be a Buffer')
  assert.ok(result.docxBuffer.length > 0, 'docxBuffer should not be empty')
  assert.match(result.proposalId, /^prop_[A-Z0-9]{26}$/)
  assert.equal(result.templateVersion, '1.0.0')
})

test('renderDocument output is a valid .docx (PizZip parseable)', async (t) => {
  mockS3Send = async (command) => {
    const key = command.params?.Key || ''
    if (key.includes('logo')) {
      const err = new Error('NoSuchKey')
      err.name = 'NoSuchKey'
      throw err
    }
    throw new Error(`Unexpected S3 key: ${key}`)
  }

  const mockLogger = { info: () => {}, warn: () => {}, debug: () => {}, error: () => {} }
  const mockS3Client = { send: (cmd) => mockS3Send(cmd) }

  const result = await renderDocument(minimalPayload, mockS3Client, mockLogger)

  // Verify it's a valid ZIP/docx
  let zip
  assert.doesNotThrow(() => {
    zip = new PizZip(result.docxBuffer)
  })

  // Verify insuredName was rendered into document.xml
  const documentXml = zip.file('word/document.xml').asText()
  assert.ok(documentXml.includes('Acme Church'), 'Rendered document should contain insuredName')
})

test('renderDocument proposalNumber uses payload value when provided', async (t) => {
  const payload = { ...minimalPayload, proposalNumber: 'PROP-2026-TEST' }
  mockS3Send = async (command) => {
    const key = command.params?.Key || ''
    if (key.includes('logo')) { const e = new Error('NoSuchKey'); e.name = 'NoSuchKey'; throw e }
    throw new Error(`Unexpected: ${key}`)
  }
  const mockLogger = { info: () => {}, warn: () => {}, debug: () => {}, error: () => {} }
  const mockS3Client = { send: (cmd) => mockS3Send(cmd) }
  const result = await renderDocument(payload, mockS3Client, mockLogger)
  assert.equal(result.proposalNumber, 'PROP-2026-TEST')
})
