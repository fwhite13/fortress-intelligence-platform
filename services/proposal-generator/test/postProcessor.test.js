import { test, mock } from 'node:test'
import assert from 'node:assert/strict'
import PizZip from 'pizzip'

// Build a minimal valid .docx buffer with word/settings.xml
function buildMinimalDocxWithSettings(settingsXml) {
  const zip = new PizZip()
  zip.file('[Content_Types].xml', `<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
  <Default Extension="xml" ContentType="application/xml"/>
  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
  <Override PartName="/word/settings.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml"/>
</Types>`)
  zip.file('_rels/.rels', `<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
</Relationships>`)
  zip.file('word/_rels/document.xml.rels', `<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
</Relationships>`)
  zip.file('word/document.xml', `<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
  <w:body><w:p><w:r><w:t>Test</w:t></w:r></w:p></w:body>
</w:document>`)
  zip.file('word/settings.xml', settingsXml)
  return zip.generate({ type: 'nodebuffer' })
}

// Mock child_process to make soffice unavailable by default
let mockExecFileBehavior = 'fail'  // 'fail' | 'succeed'

mock.module('child_process', {
  namedExports: {
    execFile: (cmd, args, options, callback) => {
      // Handle promisify signature: (cmd, args, options, callback) or (cmd, args, callback)
      const cb = typeof options === 'function' ? options : callback
      if (mockExecFileBehavior === 'fail') {
        cb(new Error('soffice: command not found'))
      } else {
        cb(null, { stdout: 'OK', stderr: '' })
      }
    }
  }
})

// Mock fs/promises for controlled file I/O
let mockFsFiles = new Map()

mock.module('fs/promises', {
  namedExports: {
    mkdir: async (path, opts) => { /* no-op */ },
    writeFile: async (path, data) => {
      mockFsFiles.set(path, data)
    },
    readFile: async (path) => {
      if (mockFsFiles.has(path)) return mockFsFiles.get(path)
      const err = new Error(`ENOENT: no such file: ${path}`)
      err.code = 'ENOENT'
      throw err
    },
    rm: async (path, opts) => { /* no-op */ },
    access: async (path) => {
      if (!mockFsFiles.has(path)) {
        const err = new Error(`ENOENT: ${path}`)
        err.code = 'ENOENT'
        throw err
      }
    },
  }
})

const { injectUpdateFields, postProcess } = await import('../src/services/postProcessor.js')

const mockLogger = {
  info: () => {},
  warn: () => {},
  debug: () => {},
  error: () => {},
}

test('injectUpdateFields: adds updateFields to settings.xml', (t) => {
  const settingsXml = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:settings xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
  <w:evenAndOddHeaders/>
</w:settings>`
  const docxBuf = buildMinimalDocxWithSettings(settingsXml)
  const result = injectUpdateFields(docxBuf)

  const zip = new PizZip(result)
  const updatedSettings = zip.file('word/settings.xml').asText()
  assert.ok(updatedSettings.includes('w:updateFields'), 'Should contain w:updateFields')
  assert.ok(updatedSettings.includes('w:val="true"'), 'Should set val=true')
})

test('injectUpdateFields: does not duplicate updateFields if already present', (t) => {
  const settingsXml = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:settings xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
  <w:updateFields w:val="true"/>
</w:settings>`
  const docxBuf = buildMinimalDocxWithSettings(settingsXml)
  const result = injectUpdateFields(docxBuf)

  const zip = new PizZip(result)
  const updatedSettings = zip.file('word/settings.xml').asText()
  // Count occurrences
  const count = (updatedSettings.match(/w:updateFields/g) || []).length
  assert.equal(count, 1, 'Should not duplicate w:updateFields')
})

test('postProcess: soffice unavailable → falls back gracefully with warning', async (t) => {
  mockExecFileBehavior = 'fail'
  mockFsFiles.clear()

  const settingsXml = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:settings xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
</w:settings>`
  const docxBuf = buildMinimalDocxWithSettings(settingsXml)

  const result = await postProcess(docxBuf, 'test-proposal-01', 'docx', mockLogger)

  assert.ok(result.docxBuffer instanceof Buffer, 'Should return docxBuffer as fallback')
  assert.equal(result.pdfBuffer, null, 'pdfBuffer should be null')
  assert.ok(result.warnings.length > 0, 'Should have at least one warning')
  assert.ok(result.warnings[0].includes('field update failed') || result.warnings[0].includes('LibreOffice'), 'Warning should mention LibreOffice failure')
})

test('postProcess: outputFormat=pdf → docxBuffer is null', async (t) => {
  mockExecFileBehavior = 'fail'
  mockFsFiles.clear()

  const settingsXml = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:settings xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
</w:settings>`
  const docxBuf = buildMinimalDocxWithSettings(settingsXml)

  const result = await postProcess(docxBuf, 'test-proposal-02', 'pdf', mockLogger)

  assert.equal(result.docxBuffer, null, 'docxBuffer should be null when outputFormat=pdf')
  // pdfBuffer will be null too since soffice fails — that's OK
  assert.ok(Array.isArray(result.warnings), 'warnings should be an array')
})

test('postProcess: outputFormat=both → warnings contain pdf failure when soffice down', async (t) => {
  mockExecFileBehavior = 'fail'
  mockFsFiles.clear()

  const settingsXml = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:settings xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
</w:settings>`
  const docxBuf = buildMinimalDocxWithSettings(settingsXml)

  const result = await postProcess(docxBuf, 'test-proposal-03', 'both', mockLogger)

  assert.ok(result.docxBuffer instanceof Buffer, 'docxBuffer should fallback')
  assert.equal(result.pdfBuffer, null, 'pdfBuffer null when soffice fails')
  // Should have warnings about both field update and pdf failures
  assert.ok(result.warnings.length >= 1)
})
