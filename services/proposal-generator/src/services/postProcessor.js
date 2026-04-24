// src/services/postProcessor.js
import { execFile } from 'child_process'
import { promisify } from 'util'
import { readFile, writeFile, rm, mkdir } from 'fs/promises'
import { join } from 'path'
import PizZip from 'pizzip'

const execFileAsync = promisify(execFile)

const TMPFS_BASE = process.env.PROPOSALS_TMPFS || '/tmp/proposals'
const SOFFICE_PATH = process.env.SOFFICE_PATH || 'soffice'
const SOFFICE_TIMEOUT_MS = parseInt(process.env.SOFFICE_TIMEOUT_MS || '30000', 10)

/**
 * Inject <w:updateFields w:val="true"/> into word/settings.xml of a .docx buffer.
 * Belt-and-suspenders: ensures Word desktop re-updates fields on open.
 * Returns updated Buffer.
 */
export function injectUpdateFields(docxBuffer) {
  const zip = new PizZip(docxBuffer)

  let settingsXml = zip.file('word/settings.xml')?.asText() || ''

  // Add updateFields if not already present
  if (!settingsXml.includes('w:updateFields')) {
    if (settingsXml.includes('</w:settings>')) {
      settingsXml = settingsXml.replace(
        '</w:settings>',
        '  <w:updateFields w:val="true"/>\n</w:settings>'
      )
    } else if (settingsXml.length > 0) {
      // settings.xml exists but has no closing tag — append
      settingsXml += '  <w:updateFields w:val="true"/>'
    } else {
      // settings.xml missing entirely — create minimal one
      settingsXml = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>\n<w:settings xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">\n  <w:updateFields w:val="true"/>\n</w:settings>'
    }
    zip.file('word/settings.xml', settingsXml)
  }

  return zip.generate({ type: 'nodebuffer', compression: 'DEFLATE' })
}

/**
 * Check if LibreOffice sidecar is accessible.
 * Called at server startup. Does not throw — just logs.
 */
export async function checkSidecarHealth(logger) {
  try {
    const { stdout } = await execFileAsync(SOFFICE_PATH, ['--version'], { timeout: 5000 })
    logger?.info({ version: stdout.trim() }, 'LibreOffice sidecar reachable')
    return true
  } catch (err) {
    logger?.warn({ err: err.message }, 'LibreOffice sidecar unreachable — service will run in fallback mode')
    return false
  }
}

/**
 * Post-process a .docx buffer through LibreOffice.
 *
 * Steps:
 * 1. Inject <w:updateFields> into settings.xml (belt-and-suspenders)
 * 2. Write .docx to per-proposal tmpfs directory
 * 3. Run soffice --headless --convert-to docx (field update)
 * 4. If outputFormat includes PDF: run soffice --headless --convert-to pdf
 * 5. Read back results
 * 6. Clean up tmpfs files
 *
 * On timeout or crash: fall back to injectUpdateFields-only result. Log warning.
 *
 * @param {Buffer} docxBuffer
 * @param {string} proposalId
 * @param {string} outputFormat - "docx" | "pdf" | "both"
 * @param {Object} logger
 * @returns {Promise<{ docxBuffer: Buffer|null, pdfBuffer: Buffer|null, warnings: string[] }>}
 */
export async function postProcess(docxBuffer, proposalId, outputFormat, logger) {
  const warnings = []

  // Always inject updateFields as belt-and-suspenders
  const docxWithFields = injectUpdateFields(docxBuffer)

  // Attempt LibreOffice post-processing
  const workDir = join(TMPFS_BASE, proposalId)
  const inputPath = join(workDir, 'input.docx')

  try {
    await mkdir(workDir, { recursive: true })
    const loOutDir = join(workDir, 'out')
    await mkdir(loOutDir, { recursive: true })
    await writeFile(inputPath, docxWithFields)

    // Step 1: Field update (re-save as docx to update TOC etc.)
    const env = { ...process.env, HOME: process.env.HOME || '/tmp' }
    let updatedDocxBuffer = docxWithFields  // fallback
    try {
      await execFileAsync(SOFFICE_PATH, [
        '--headless',
        '--norestore',
        '--infilter=Microsoft Word 2007-2019 XML',
        '--convert-to', 'docx',
        '--outdir', loOutDir,
        inputPath,
      ], { timeout: SOFFICE_TIMEOUT_MS, env })

      updatedDocxBuffer = await readFile(join(loOutDir, 'input.docx'))
      logger?.info({ proposalId }, 'LibreOffice field update complete')
    } catch (err) {
      logger?.warn({ proposalId, err: err.message }, 'LibreOffice field update failed — using injectUpdateFields fallback')
      warnings.push(`LibreOffice field update failed: ${err.message}. Document will update fields on first open in Word.`)
      // updatedDocxBuffer remains docxWithFields (fallback)
    }

    // Step 2: PDF generation (if requested)
    let pdfBuffer = null
    if (outputFormat === 'pdf' || outputFormat === 'both') {
      try {
        const pdfSourcePath = join(workDir, 'pdf-source.docx')
        await writeFile(pdfSourcePath, updatedDocxBuffer)
        await execFileAsync(SOFFICE_PATH, [
          '--headless',
          '--convert-to', 'pdf',
          '--outdir', loOutDir,
          pdfSourcePath,
        ], { timeout: SOFFICE_TIMEOUT_MS, env })
        pdfBuffer = await readFile(join(loOutDir, 'pdf-source.pdf'))
        logger?.info({ proposalId }, 'LibreOffice PDF conversion complete')
      } catch (err) {
        logger?.warn({ proposalId, err: err.message }, 'LibreOffice PDF conversion failed')
        warnings.push(`PDF generation failed: ${err.message}. Returning .docx only.`)
      }
    }

    return {
      docxBuffer: outputFormat === 'pdf' ? null : updatedDocxBuffer,
      pdfBuffer,
      warnings,
    }
  } finally {
    // Clean up tmpfs — best effort
    try {
      await rm(workDir, { recursive: true, force: true })
    } catch (err) {
      logger?.warn({ proposalId, err: err.message }, 'Failed to clean up tmpfs directory')
    }
  }
}
