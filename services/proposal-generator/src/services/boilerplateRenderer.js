// src/services/boilerplateRenderer.js
import PizZip from 'pizzip'
import Docxtemplater from 'docxtemplater'
import { GetObjectCommand } from '@aws-sdk/client-s3'

/**
 * Simple {varName} substitution in a string.
 * Replaces all occurrences of {key} with templateData[key] ?? ''
 */
function substituteVars(content, templateData) {
  return content.replace(/\{([a-zA-Z_][a-zA-Z0-9_]*)\}/g, (match, key) => {
    const val = templateData[key]
    return val !== null && val !== undefined ? String(val) : ''
  })
}

/**
 * Same as substituteVars but XML-escapes each substituted value.
 */
function substituteVarsEscaped(content, templateData) {
  return content.replace(/\{([a-zA-Z_][a-zA-Z0-9_]*)\}/g, (match, key) => {
    const val = templateData[key]
    const str = val !== null && val !== undefined ? String(val) : ''
    return str.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
  })
}

/**
 * Extract body XML from a .docx buffer (strips sectPr).
 */
function extractBodyXml(docxBuffer) {
  const zip = new PizZip(docxBuffer)
  const documentXml = zip.file('word/document.xml').asText()
  const bodyMatch = documentXml.match(/<w:body>([\s\S]*?)<\/w:body>/)
  if (!bodyMatch) return ''
  let bodyContent = bodyMatch[1]
  bodyContent = bodyContent.replace(/<w:sectPr\b[\s\S]*?<\/w:sectPr>\s*$/, '')
  bodyContent = bodyContent.replace(/<w:sectPr\b[^>]*\/>\s*$/, '')
  return bodyContent.trim()
}

async function streamToBuffer(stream) {
  const chunks = []
  for await (const chunk of stream) {
    chunks.push(chunk instanceof Buffer ? chunk : Buffer.from(chunk))
  }
  return Buffer.concat(chunks)
}

/**
 * Render all boilerplate blocks into a concatenated WordML XML string.
 */
export async function renderBoilerplate(boilerplateJson, selections, exclusions, defaultBoilerplate, templateData, s3Client, bucketName, logger) {
  const blocks = boilerplateJson?.blocks || {}

  // Determine active block IDs
  let activeIds = []
  if (selections && selections.length > 0) {
    activeIds = [...selections]
  } else if (defaultBoilerplate && defaultBoilerplate.length > 0) {
    activeIds = [...defaultBoilerplate]
  }

  // Apply exclusions
  const excl = exclusions || []
  if (excl.length > 0) {
    activeIds = activeIds.filter(id => !excl.includes(id))
  }

  const xmlParts = []

  for (const blockId of activeIds) {
    const block = blocks[blockId]
    if (!block) {
      logger?.warn({ blockId }, 'Boilerplate block not found in registry — skipping')
      continue
    }

    try {
      if (block.type === 'text') {
        const substituted = substituteVars(block.content || '', templateData)
        // Escape XML special chars and wrap in a paragraph
        const escaped = substituted
          .replace(/&/g, '&amp;')
          .replace(/</g, '&lt;')
          .replace(/>/g, '&gt;')
        xmlParts.push(`<w:p><w:r><w:t xml:space="preserve">${escaped}</w:t></w:r></w:p>`)
      } else if (block.type === 'wordml') {
        const substituted = substituteVarsEscaped(block.content || '', templateData)
        xmlParts.push(substituted)
      } else if (block.type === 'partial') {
        const key = block.partialKey
        if (!key) {
          logger?.warn({ blockId }, 'Boilerplate partial missing partialKey — skipping')
          continue
        }
        const response = await s3Client.send(new GetObjectCommand({ Bucket: bucketName, Key: key }))
        const buf = await streamToBuffer(response.Body)

        // Render the partial as a mini-template with templateData
        const zip = new PizZip(buf)
        const doc = new Docxtemplater(zip, {
          paragraphLoop: true,
          linebreaks: true,
          modules: [],
          nullGetter(part) {
            return part.module ? null : ''
          },
        })
        doc.render(templateData)
        const rendered = doc.getZip().generate({ type: 'nodebuffer' })
        xmlParts.push(extractBodyXml(rendered))
      }
    } catch (err) {
      logger?.warn({ blockId, err: err.message }, 'Failed to render boilerplate block — skipping')
    }
  }

  return xmlParts.join('\n')
}
