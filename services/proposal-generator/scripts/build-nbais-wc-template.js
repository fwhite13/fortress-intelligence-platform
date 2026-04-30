#!/usr/bin/env node
/**
 * build-nbais-wc-template.js
 *
 * Generates templates/verticals/nbais-wc/master.docx — a docxtemplater-compatible
 * Word template that visually matches Jay's NBAIS WC Proposal PDF design.
 *
 * Merge fields use docxtemplater {tag} syntax. The assembleTemplateData.js
 * nbais-wc branch populates these at render time.
 *
 * Usage: node scripts/build-nbais-wc-template.js
 */

import {
  Document, Packer, Paragraph, TextRun, Table, TableRow, TableCell,
  WidthType, AlignmentType, BorderStyle, HeadingLevel, ImageRun,
  SectionType, PageBreak, Header, Footer, TableOfContents,
  ShadingType, VerticalAlign, convertInchesToTwip, Tab, TabStopType,
  TabStopPosition,
} from 'docx'
import { writeFileSync, readFileSync } from 'fs'
import { join, dirname } from 'path'
import { fileURLToPath } from 'url'

const __dirname = dirname(fileURLToPath(import.meta.url))
const OUTDIR = join(__dirname, '..', 'templates', 'verticals', 'nbais-wc')

// ─── Colors ────────────────────────────────────────────────────────────
const NAVY = '1F3864'
const BLUE = '2E75B6'
const LT_BLUE = 'EBF3FB'
const WHITE = 'FFFFFF'
const GRAY = '595959'
const MID_GRAY = 'B0B0B0'
const LIGHT_GRAY = 'F5F5F5'
const BORDER_COLOR = 'CCCCCC'
const RED = 'C00000'
const BLACK = '222222'

// ─── Shared styling helpers ────────────────────────────────────────────
const FONT = 'Helvetica Neue'
const BODY_SIZE = 20 // half-points → 10pt
const SMALL_SIZE = 19 // 9.5pt
const TINY_SIZE = 17 // 8.5pt
const FOOTER_SIZE = 15 // 7.5pt

function bodyText(text, opts = {}) {
  return new TextRun({
    text,
    font: FONT,
    size: opts.size || BODY_SIZE,
    color: opts.color || BLACK,
    bold: opts.bold || false,
    italics: opts.italics || false,
    ...(opts.break ? { break: opts.break } : {}),
  })
}

function mergeField(tag) {
  // docxtemplater uses {tag} syntax
  return new TextRun({
    text: `{${tag}}`,
    font: FONT,
    size: BODY_SIZE,
    color: BLACK,
  })
}

function navyBanner(text) {
  return new Paragraph({
    shading: { type: ShadingType.CLEAR, fill: NAVY },
    spacing: { before: 0, after: 200 },
    children: [
      new TextRun({
        text,
        font: FONT,
        size: 28, // 14pt
        bold: true,
        color: WHITE,
      }),
    ],
    indent: { left: convertInchesToTwip(0.1), right: convertInchesToTwip(0.1) },
  })
}

function navyBannerContinued(text) {
  return new Paragraph({
    shading: { type: ShadingType.CLEAR, fill: NAVY },
    spacing: { before: 0, after: 200 },
    children: [
      new TextRun({
        text,
        font: FONT,
        size: 26, // 13pt
        bold: true,
        color: WHITE,
      }),
    ],
    indent: { left: convertInchesToTwip(0.1), right: convertInchesToTwip(0.1) },
  })
}

function sectionDivider(text) {
  return new Paragraph({
    spacing: { before: 200, after: 100 },
    border: { bottom: { style: BorderStyle.SINGLE, size: 1, color: BORDER_COLOR } },
    children: [
      new TextRun({
        text,
        font: FONT,
        size: 24, // 12pt
        bold: true,
        color: NAVY,
      }),
    ],
  })
}

function h3(text) {
  return new Paragraph({
    spacing: { before: 200, after: 120 },
    border: { bottom: { style: BorderStyle.SINGLE, size: 1, color: BORDER_COLOR } },
    children: [
      new TextRun({
        text,
        font: FONT,
        size: 22, // 11pt
        bold: true,
        color: NAVY,
      }),
    ],
  })
}

function h3Blue(text) {
  return new Paragraph({
    spacing: { before: 120, after: 60 },
    children: [
      new TextRun({
        text,
        font: FONT,
        size: 20, // 10pt
        bold: true,
        color: BLUE,
      }),
    ],
  })
}

function bodyPara(text, opts = {}) {
  return new Paragraph({
    spacing: { before: opts.spaceBefore || 0, after: opts.spaceAfter || 80 },
    children: [bodyText(text, opts)],
  })
}

function leadPara(text) {
  return new Paragraph({
    spacing: { before: 0, after: 140 },
    children: [
      new TextRun({
        text,
        font: FONT,
        size: SMALL_SIZE,
        color: GRAY,
        italics: true,
      }),
    ],
  })
}

function finePara(text, opts = {}) {
  return new Paragraph({
    spacing: { before: opts.spaceBefore || 0, after: opts.spaceAfter || 60 },
    children: [
      new TextRun({
        text,
        font: FONT,
        size: TINY_SIZE,
        color: GRAY,
      }),
    ],
  })
}

function bulletItem(text, opts = {}) {
  return new Paragraph({
    bullet: { level: 0 },
    spacing: { before: 0, after: 40 },
    children: [
      new TextRun({
        text,
        font: FONT,
        size: opts.size || SMALL_SIZE,
        color: opts.color || BLACK,
      }),
    ],
  })
}

function pageBreakPara() {
  return new Paragraph({ children: [new PageBreak()] })
}

// ─── Table helpers ─────────────────────────────────────────────────────
const thinBorder = { style: BorderStyle.SINGLE, size: 1, color: BORDER_COLOR }
const navyBorderStyle = { style: BorderStyle.SINGLE, size: 1, color: NAVY }
const noBorder = { style: BorderStyle.NONE, size: 0, color: WHITE }

function kvRow(label, value, isEven = false) {
  const shading = isEven ? { type: ShadingType.CLEAR, fill: LIGHT_GRAY } : undefined
  return new TableRow({
    children: [
      new TableCell({
        width: { size: 35, type: WidthType.PERCENTAGE },
        shading,
        borders: { top: thinBorder, bottom: thinBorder, left: thinBorder, right: thinBorder },
        verticalAlign: VerticalAlign.CENTER,
        children: [new Paragraph({
          children: [new TextRun({ text: label, font: FONT, size: 18, bold: true, color: BLACK })],
        })],
      }),
      new TableCell({
        width: { size: 65, type: WidthType.PERCENTAGE },
        shading,
        borders: { top: thinBorder, bottom: thinBorder, left: thinBorder, right: thinBorder },
        verticalAlign: VerticalAlign.CENTER,
        children: [new Paragraph({
          children: typeof value === 'string'
            ? [new TextRun({ text: value, font: FONT, size: 18, color: BLACK })]
            : value,
        })],
      }),
    ],
  })
}

function navyHeaderRow(cols) {
  return new TableRow({
    tableHeader: true,
    children: cols.map(({ text, width, align }) =>
      new TableCell({
        width: width ? { size: width, type: WidthType.PERCENTAGE } : undefined,
        shading: { type: ShadingType.CLEAR, fill: NAVY },
        borders: { top: navyBorderStyle, bottom: navyBorderStyle, left: navyBorderStyle, right: navyBorderStyle },
        verticalAlign: VerticalAlign.CENTER,
        children: [new Paragraph({
          alignment: align || AlignmentType.LEFT,
          children: [new TextRun({
            text: text.toUpperCase(),
            font: FONT,
            size: 17,
            bold: true,
            color: WHITE,
          })],
        })],
      })
    ),
  })
}

function dataRow(cells, isEven = false) {
  const shading = isEven ? { type: ShadingType.CLEAR, fill: LIGHT_GRAY } : undefined
  return new TableRow({
    children: cells.map(({ content, align }) => {
      const children = typeof content === 'string'
        ? [new TextRun({ text: content, font: FONT, size: 18, color: BLACK })]
        : content
      return new TableCell({
        shading,
        borders: { top: thinBorder, bottom: thinBorder, left: thinBorder, right: thinBorder },
        verticalAlign: VerticalAlign.CENTER,
        children: [new Paragraph({ alignment: align || AlignmentType.LEFT, children })],
      })
    }),
  })
}

// ─── Page header/footer (used for interior pages) ──────────────────────
function interiorHeader() {
  return new Header({
    children: [
      new Paragraph({
        border: { bottom: { style: BorderStyle.SINGLE, size: 3, color: NAVY, space: 6 } },
        spacing: { after: 200 },
        children: [
          // Logo placeholder — docxtemplater image tag
          new TextRun({ text: '{%horizontalLogoBase64}', font: FONT, size: 16 }),
          new TextRun({ text: '\t' }),
          new TextRun({
            text: "Workers' Compensation Proposal",
            font: FONT,
            size: 18,
            italics: true,
            color: GRAY,
          }),
        ],
        tabStops: [{ type: TabStopType.RIGHT, position: convertInchesToTwip(7.4) }],
      }),
    ],
  })
}

function interiorFooter() {
  return new Footer({
    children: [
      new Paragraph({
        border: { top: { style: BorderStyle.SINGLE, size: 1, color: MID_GRAY, space: 4 } },
        spacing: { before: 80 },
        children: [
          new TextRun({
            text: "NBAIS Workers' Compensation Proposal · ",
            font: FONT,
            size: FOOTER_SIZE,
            color: GRAY,
          }),
          new TextRun({
            text: '{memberName}',
            font: FONT,
            size: FOOTER_SIZE,
            color: GRAY,
          }),
          new TextRun({
            text: ' · Confidential',
            font: FONT,
            size: FOOTER_SIZE,
            color: GRAY,
          }),
          new TextRun({ text: '\t' }),
          new TextRun({
            text: '{currentPageLabel}',
            font: FONT,
            size: FOOTER_SIZE,
            color: GRAY,
          }),
        ],
        tabStops: [{ type: TabStopType.RIGHT, position: convertInchesToTwip(7.4) }],
      }),
    ],
  })
}

// ═══════════════════════════════════════════════════════════════════════
// PAGE 1 — COVER
// ═══════════════════════════════════════════════════════════════════════
function coverPage() {
  return [
    // Navy top rule
    new Paragraph({
      shading: { type: ShadingType.CLEAR, fill: NAVY },
      spacing: { before: 0, after: 0 },
      children: [new TextRun({ text: ' ', font: FONT, size: 10 })],
    }),
    // Eyebrow
    new Paragraph({
      spacing: { before: 200, after: 0 },
      children: [
        new TextRun({
          text: 'Nevada Builders Alliance Insurance Solutions',
          font: FONT,
          size: 18,
          color: GRAY,
          italics: true,
        }),
      ],
    }),
    // Stacked logo placeholder
    new Paragraph({
      alignment: AlignmentType.CENTER,
      spacing: { before: 600, after: 400 },
      children: [
        new TextRun({ text: '{%stackedLogoBase64}', font: FONT, size: 16 }),
      ],
    }),
    // Title
    new Paragraph({
      alignment: AlignmentType.CENTER,
      spacing: { before: 200, after: 100 },
      children: [
        new TextRun({
          text: "Workers' Compensation",
          font: FONT,
          size: 60, // 30pt
          bold: true,
          color: NAVY,
        }),
      ],
    }),
    new Paragraph({
      alignment: AlignmentType.CENTER,
      spacing: { before: 0, after: 200 },
      children: [
        new TextRun({
          text: 'Insurance Proposal',
          font: FONT,
          size: 60,
          bold: true,
          color: NAVY,
        }),
      ],
    }),
    // Subtitle
    new Paragraph({
      alignment: AlignmentType.CENTER,
      spacing: { before: 0, after: 600 },
      children: [
        new TextRun({
          text: 'Prepared exclusively for Nevada Builders Alliance members',
          font: FONT,
          size: 22,
          color: GRAY,
          italics: true,
        }),
      ],
    }),
    // Cover meta grid (using a table for the label/value layout)
    new Table({
      width: { size: 60, type: WidthType.PERCENTAGE },
      alignment: AlignmentType.CENTER,
      rows: [
        coverMetaRow('Prepared For', '{memberName}'),
        coverMetaRow('Policy Period', '{policyPeriodDisplay}'),
        coverMetaRow('Prepared By', 'Dianne Slater'),
        coverMetaRow('Date', '{quoteDate}'),
        coverMetaRow('Program', 'Nevada Builders Alliance — NBAIS Member Program'),
      ],
    }),
    // Spacer
    new Paragraph({ spacing: { before: 600, after: 0 }, children: [] }),
    // Bottom rule + confidential
    new Paragraph({
      border: { top: { style: BorderStyle.SINGLE, size: 2, color: NAVY } },
      alignment: AlignmentType.CENTER,
      spacing: { before: 200, after: 0 },
      children: [
        new TextRun({
          text: "Confidential — Prepared for the named member's exclusive use",
          font: FONT,
          size: TINY_SIZE,
          color: GRAY,
          italics: true,
        }),
      ],
    }),
  ]
}

function coverMetaRow(label, value) {
  return new TableRow({
    children: [
      new TableCell({
        width: { size: 35, type: WidthType.PERCENTAGE },
        borders: { top: noBorder, bottom: noBorder, left: noBorder, right: noBorder },
        children: [new Paragraph({
          alignment: AlignmentType.RIGHT,
          spacing: { after: 80 },
          children: [new TextRun({
            text: label,
            font: FONT,
            size: BODY_SIZE,
            bold: true,
            color: NAVY,
          })],
        })],
      }),
      new TableCell({
        width: { size: 65, type: WidthType.PERCENTAGE },
        borders: { top: noBorder, bottom: noBorder, left: noBorder, right: noBorder },
        children: [new Paragraph({
          spacing: { after: 80 },
          indent: { left: convertInchesToTwip(0.15) },
          children: [new TextRun({
            text: value,
            font: FONT,
            size: BODY_SIZE,
            color: BLACK,
          })],
        })],
      }),
    ],
  })
}

// ═══════════════════════════════════════════════════════════════════════
// PAGE 2 — COVER LETTER
// ═══════════════════════════════════════════════════════════════════════
function coverLetterPage() {
  return [
    // Date + member info
    bodyPara('{quoteDate}'),
    bodyPara('{memberName}', { spaceBefore: 100 }),
    bodyPara('{memberAddress}'),
    // RE line
    new Paragraph({
      spacing: { before: 80, after: 120 },
      children: [
        bodyText('RE: ', { bold: true }),
        bodyText("Workers' Compensation Insurance Proposal — Nevada Builders Alliance Member Program", { bold: true }),
      ],
    }),
    bodyPara('Dear {memberName},', { spaceBefore: 120 }),
    // About this proposal
    h3('About this proposal'),
    bodyPara("On behalf of Nevada Builders Alliance Insurance Services (NBAIS), we are pleased to present this Workers' Compensation Insurance proposal exclusively for members of the Nevada Builders Alliance (NBA). This proposal has been prepared specifically for your organization and reflects the competitive program rates and enhanced coverage options available through your NBA membership."),
    bodyPara("NBAIS was established to serve the unique risk management needs of Nevada's construction industry — from residential and commercial builders to specialty trade contractors. As an NBA member, your organization has access to a Workers' Compensation program designed around the realities of your trade, not a one-size-fits-all solution."),
    // Program highlights
    h3('Program highlights'),
    bulletItem('Exclusive NBA member pricing — competitive group rates unavailable in the open market'),
    bulletItem('Construction-class expertise — underwriting specialists who understand your trade'),
    bulletItem('Dividend potential — SIG participation with return of premium for favorable loss performance'),
    bulletItem('Loss control resources — proactive safety and claims management support'),
    bulletItem('Dedicated service team — NBAIS producers with direct carrier access'),
    // What is included
    h3('What is included in this proposal'),
    bodyPara('This proposal package contains the following for your review:'),
    bulletItem('Premium Summary & Coverage at a Glance — a summary of your proposed coverage terms and estimated premium.'),
    bulletItem("Workers' Compensation Coverage Details — a detailed outline of the proposed coverage terms, limits, and exclusions applicable to your operation."),
    bulletItem('Carrier Quote — the carrier quotation secured for your review, including the class code and payroll basis used to develop this quote. Please review for accuracy and notify us of any changes prior to binding.'),
    bulletItem('Coverage Recommendations — a comprehensive list of additional coverage lines for your consideration across commercial, personal, bond, employee benefits, and life planning categories.'),
  ]
}

// ═══════════════════════════════════════════════════════════════════════
// PAGE 3 — PREMIUM SUMMARY
// ═══════════════════════════════════════════════════════════════════════
function premiumSummaryPage() {
  return [
    navyBanner('Premium Summary'),
    leadPara('Your estimated cost for the coverage period {policyPeriodDisplay}. All figures are subject to final payroll audit.'),

    // Coverage at a Glance table
    new Table({
      width: { size: 100, type: WidthType.PERCENTAGE },
      rows: [
        // Banner header row
        new TableRow({
          children: [
            new TableCell({
              columnSpan: 2,
              shading: { type: ShadingType.CLEAR, fill: NAVY },
              borders: { top: navyBorderStyle, bottom: navyBorderStyle, left: navyBorderStyle, right: navyBorderStyle },
              children: [new Paragraph({
                children: [new TextRun({
                  text: 'Coverage at a Glance',
                  font: FONT,
                  size: 22,
                  bold: true,
                  color: WHITE,
                })],
              })],
            }),
          ],
        }),
        kvRow('Insured', '{memberName}', true),
        kvRow('Policy Period', '{policyPeriodDisplay}'),
        kvRow('Coverage', "Workers' Compensation — Statutory (Nevada) / Employers' Liability", true),
        kvRow('Employers\' Liability Limits', '$1,000,000 Each Accident / $1,000,000 Disease – Each Employee / $1,000,000 Disease – Policy Limit'),
        kvRow('Program', 'Nevada Builders Alliance — NBAIS Member Program', true),
        kvRow('Carrier', 'Builders Association of Western Nevada Self-Insured Group (BAWNSIG)'),
        kvRow('Est. Premium', [
          new TextRun({ text: '{estPremium}', font: FONT, size: 18, color: BLACK }),
          new TextRun({ text: ' (subject to final audit)', font: FONT, size: 18, color: BLACK }),
        ], true),
        kvRow('Surplus Contribution (8%)', '{surplusContribution}'),
        kvRow('Employers\' Liability Fee', '{employersLiabilityFee}', true),
        // Total row with highlight
        new TableRow({
          children: [
            new TableCell({
              width: { size: 35, type: WidthType.PERCENTAGE },
              shading: { type: ShadingType.CLEAR, fill: LT_BLUE },
              borders: { top: thinBorder, bottom: thinBorder, left: thinBorder, right: thinBorder },
              children: [new Paragraph({
                children: [new TextRun({
                  text: 'Total Estimated Cost',
                  font: FONT, size: 20, bold: true, color: NAVY,
                })],
              })],
            }),
            new TableCell({
              width: { size: 65, type: WidthType.PERCENTAGE },
              shading: { type: ShadingType.CLEAR, fill: LT_BLUE },
              borders: { top: thinBorder, bottom: thinBorder, left: thinBorder, right: thinBorder },
              children: [new Paragraph({
                children: [new TextRun({
                  text: '{totalEstimatedPremium}',
                  font: FONT, size: 20, bold: true, color: NAVY,
                })],
              })],
            }),
          ],
        }),
        kvRow('Initial Down Payment', [
          new TextRun({ text: '{downPayment}', font: FONT, size: 18, color: BLACK }),
          new TextRun({ text: ' (25% — new business). Balance payable online via secure payment link provided upon binding.', font: FONT, size: 18, color: BLACK }),
        ]),
      ],
    }),

    h3("What's next"),
    bodyPara('Review the Coverage Details on the following page, confirm payroll and class code accuracy, and contact your NBAIS producer to bind. Final premium will be reconciled at audit.'),
  ]
}

// ═══════════════════════════════════════════════════════════════════════
// PAGE 4 — COVERAGE DETAILS (1 of 2)
// ═══════════════════════════════════════════════════════════════════════
function coverageDetails1Page() {
  return [
    navyBanner("Coverage Details — Workers' Compensation"),

    h3('Policy Information'),
    new Table({
      width: { size: 100, type: WidthType.PERCENTAGE },
      rows: [
        kvRow('Carrier', 'Builders Association of Western Nevada Self-Insured Group (BAWNSIG)'),
        kvRow('Program Manager', 'Lusense', true),
        kvRow('Financial Strength', 'BAWNSIG is a Nevada state-regulated self-insured group. AM Best rating not applicable — see program disclosure.'),
        kvRow('Policy Period', '{policyPeriodDisplay}', true),
        kvRow('Coverage', "Workers' Compensation"),
        kvRow('States Covered', 'Nevada', true),
      ],
    }),

    h3('Named Insured'),
    new Paragraph({
      spacing: { before: 40, after: 160 },
      children: [new TextRun({
        text: '{memberLegalName}',
        font: FONT,
        size: 22,
        bold: true,
        color: NAVY,
      })],
    }),

    h3('Coverage and Limits'),
    new Table({
      width: { size: 100, type: WidthType.PERCENTAGE },
      rows: [
        navyHeaderRow([
          { text: 'Coverage', width: 70 },
          { text: 'Limit', width: 30, align: AlignmentType.RIGHT },
        ]),
        dataRow([
          { content: "Part I — Workers' Compensation" },
          { content: 'Statutory per State of Nevada', align: AlignmentType.RIGHT },
        ]),
        dataRow([
          { content: "Part II — Employers' Liability: Each Accident" },
          { content: '$1,000,000', align: AlignmentType.RIGHT },
        ], true),
        dataRow([
          { content: "Part II — Employers' Liability: Disease — Each Employee" },
          { content: '$1,000,000', align: AlignmentType.RIGHT },
        ]),
        dataRow([
          { content: "Part II — Employers' Liability: Disease — Policy Limit" },
          { content: '$1,000,000', align: AlignmentType.RIGHT },
        ], true),
      ],
    }),

    h3('Surplus Contribution'),
    bodyPara("As a self-insured group (SIG), BAWNSIG requires a surplus contribution in addition to the estimated premium. This contribution — calculated at 8% of the estimated premium — is a regulatory requirement for SIG participation in Nevada and supports the financial reserves of the group. It is not a fee retained by NBAIS or your producer.", { size: BODY_SIZE }),
  ]
}

// ═══════════════════════════════════════════════════════════════════════
// PAGE 5 — COVERAGE DETAILS (2 of 2)
// ═══════════════════════════════════════════════════════════════════════
function coverageDetails2Page() {
  return [
    navyBannerContinued('Coverage Details (continued)'),

    h3('Employee Classification Schedule'),

    new Table({
      width: { size: 100, type: WidthType.PERCENTAGE },
      rows: [
        navyHeaderRow([
          { text: 'State', width: 7 },
          { text: 'Class Code', width: 12 },
          { text: 'Description', width: 28 },
          { text: 'Est. Annual Payroll', width: 21, align: AlignmentType.RIGHT },
          { text: 'Rate', width: 12, align: AlignmentType.RIGHT },
          { text: 'Est. Premium', width: 20, align: AlignmentType.RIGHT },
        ]),
        // Dynamic rows — docxtemplater loop
        // The {#classSchedule} loop will repeat this row
        dataRow([
          { content: '{#classSchedule}{state}' },
          { content: '{classCode}' },
          { content: '{classDescription}' },
          { content: '{estAnnualPayroll}', align: AlignmentType.RIGHT },
          { content: '{rate}', align: AlignmentType.RIGHT },
          { content: '{classEstPremium}{/classSchedule}', align: AlignmentType.RIGHT },
        ]),
        // Total row
        new TableRow({
          children: [
            new TableCell({
              columnSpan: 5,
              shading: { type: ShadingType.CLEAR, fill: LIGHT_GRAY },
              borders: { top: thinBorder, bottom: thinBorder, left: thinBorder, right: thinBorder },
              children: [new Paragraph({
                alignment: AlignmentType.RIGHT,
                children: [new TextRun({
                  text: 'Total Estimated Premium',
                  font: FONT, size: 18, bold: true, color: BLACK,
                })],
              })],
            }),
            new TableCell({
              shading: { type: ShadingType.CLEAR, fill: LIGHT_GRAY },
              borders: { top: thinBorder, bottom: thinBorder, left: thinBorder, right: thinBorder },
              children: [new Paragraph({
                alignment: AlignmentType.RIGHT,
                children: [new TextRun({
                  text: '{estPremium}',
                  font: FONT, size: 18, bold: true, color: BLACK,
                })],
              })],
            }),
          ],
        }),
      ],
    }),

    // Excluded persons — conditional block
    // {#hasExcludedPersons}
    new Paragraph({
      spacing: { before: 0, after: 0 },
      children: [new TextRun({ text: '{#hasExcludedPersons}', font: FONT, size: 2, color: WHITE })],
    }),
    h3('Excluded Persons'),
    bodyPara("The following individuals have elected to reject workers' compensation coverage under Form D-43 (Employee's Election to Reject Coverage):"),

    new Table({
      width: { size: 100, type: WidthType.PERCENTAGE },
      rows: [
        navyHeaderRow([
          { text: 'Name', width: 50 },
          { text: 'Form', width: 50 },
        ]),
        dataRow([
          { content: '{#excludedPersons}{name}' },
          { content: 'Form D-43 — Election to Reject Coverage{/excludedPersons}' },
        ]),
      ],
    }),

    new Paragraph({
      spacing: { before: 60, after: 40 },
      children: [
        new TextRun({ text: 'D-43 form required: ', font: FONT, size: TINY_SIZE, bold: true, color: RED }),
        new TextRun({ text: 'A completed and signed Form D-43 is required for each individual listed above prior to binding. Coverage cannot be excluded without a signed D-43 on file.', font: FONT, size: TINY_SIZE, color: GRAY }),
      ],
    }),
    finePara('Important: Excluded individuals are not covered under this workers\' compensation policy. Please confirm these elections are accurate prior to binding.', { spaceBefore: 40 }),
    // {/hasExcludedPersons}
    new Paragraph({
      spacing: { before: 0, after: 0 },
      children: [new TextRun({ text: '{/hasExcludedPersons}', font: FONT, size: 2, color: WHITE })],
    }),

    // SIG Disclosure
    h3('Self-Insured Group Disclosure'),
    new Paragraph({
      shading: { type: ShadingType.CLEAR, fill: LIGHT_GRAY },
      border: { left: { style: BorderStyle.SINGLE, size: 6, color: BLUE } },
      spacing: { before: 100, after: 100 },
      indent: { left: convertInchesToTwip(0.15) },
      children: [
        new TextRun({
          text: 'BAWNSIG is a Nevada-regulated self-insured group, not a traditional insurance carrier, and therefore does not carry an AM Best financial strength rating. BAWNSIG operates under the regulatory oversight of the Nevada Division of Industrial Relations and maintains reserves in accordance with state requirements. Members of NBAIS benefit from the group\'s long-standing solvency and claims-paying history as a construction industry SIG in Nevada.',
          font: FONT,
          size: TINY_SIZE,
          color: GRAY,
        }),
      ],
    }),
  ]
}

// ═══════════════════════════════════════════════════════════════════════
// PAGE 6 — NEXT STEPS & MEMBER AUTHORIZATION
// ═══════════════════════════════════════════════════════════════════════
function nextStepsPage() {
  return [
    navyBanner('Next Steps & Member Authorization'),

    bodyPara('To bind coverage or to discuss this proposal in further detail, please contact your NBAIS producer using the information below. Please review all coverage details carefully and confirm payroll and class code accuracy prior to binding, as final premium is subject to audit.'),

    // Contact grid — two-column table
    new Table({
      width: { size: 100, type: WidthType.PERCENTAGE },
      rows: [
        new TableRow({
          children: [
            new TableCell({
              width: { size: 50, type: WidthType.PERCENTAGE },
              shading: { type: ShadingType.CLEAR, fill: LIGHT_GRAY },
              borders: { top: thinBorder, bottom: thinBorder, left: thinBorder, right: thinBorder },
              children: [
                new Paragraph({
                  spacing: { after: 80 },
                  children: [new TextRun({ text: 'Your NBAIS Producer', font: FONT, size: 20, bold: true, color: NAVY })],
                }),
                new Paragraph({
                  children: [new TextRun({ text: 'Dianne Slater', font: FONT, size: SMALL_SIZE, bold: true, color: BLACK })],
                }),
                new Paragraph({
                  children: [new TextRun({ text: '[Title]', font: FONT, size: SMALL_SIZE, italics: true, color: GRAY })],
                }),
                new Paragraph({
                  children: [new TextRun({ text: '[Phone Number]', font: FONT, size: SMALL_SIZE, italics: true, color: GRAY })],
                }),
                new Paragraph({
                  children: [new TextRun({ text: '[Email Address]', font: FONT, size: SMALL_SIZE, italics: true, color: GRAY })],
                }),
              ],
            }),
            new TableCell({
              width: { size: 50, type: WidthType.PERCENTAGE },
              shading: { type: ShadingType.CLEAR, fill: LIGHT_GRAY },
              borders: { top: thinBorder, bottom: thinBorder, left: thinBorder, right: thinBorder },
              children: [
                new Paragraph({
                  spacing: { after: 80 },
                  children: [new TextRun({ text: 'NBAIS Program Office', font: FONT, size: 20, bold: true, color: NAVY })],
                }),
                new Paragraph({
                  children: [new TextRun({ text: 'Nevada Builders Alliance Insurance Services', font: FONT, size: SMALL_SIZE, color: BLACK })],
                }),
                new Paragraph({
                  children: [new TextRun({ text: '[Office Address]', font: FONT, size: SMALL_SIZE, italics: true, color: GRAY })],
                }),
                new Paragraph({
                  children: [new TextRun({ text: '[www.nbaiswebsite.com]', font: FONT, size: SMALL_SIZE, italics: true, color: GRAY })],
                }),
              ],
            }),
          ],
        }),
      ],
    }),

    h3('Member Authorization'),
    bodyPara("By signing below, the undersigned acknowledges receipt of this Workers' Compensation Insurance proposal and authorizes Nevada Builders Alliance Insurance Services (NBAIS) to bind coverage as described herein, effective on the policy period stated above. The undersigned confirms that the payroll, classification codes, and excluded persons listed in this proposal are accurate to the best of their knowledge and understands that final premium is subject to audit. The required initial down payment will be remitted online via the secure payment link provided upon binding."),

    // Signature block
    new Table({
      width: { size: 100, type: WidthType.PERCENTAGE },
      rows: ['By', 'Print Name', 'Title', 'Date'].map(label =>
        new TableRow({
          children: [
            new TableCell({
              width: { size: 15, type: WidthType.PERCENTAGE },
              borders: { top: noBorder, bottom: noBorder, left: noBorder, right: noBorder },
              verticalAlign: VerticalAlign.BOTTOM,
              children: [new Paragraph({
                spacing: { before: 200 },
                children: [new TextRun({ text: label, font: FONT, size: BODY_SIZE, bold: true, color: NAVY })],
              })],
            }),
            new TableCell({
              width: { size: 85, type: WidthType.PERCENTAGE },
              borders: { top: noBorder, bottom: { style: BorderStyle.SINGLE, size: 1, color: BLACK }, left: noBorder, right: noBorder },
              verticalAlign: VerticalAlign.BOTTOM,
              children: [new Paragraph({ spacing: { before: 200 }, children: [] })],
            }),
          ],
        })
      ),
    }),

    // Disclaimer
    finePara("This proposal is not a binder or guarantee of coverage. All coverage is subject to underwriting approval, policy terms, conditions, and exclusions. Premium estimates are subject to final payroll audit. NBAIS is an insurance program administered on behalf of Nevada Builders Alliance members.", { spaceBefore: 200 }),
  ]
}

// ═══════════════════════════════════════════════════════════════════════
// PAGE 7 — COVERAGE RECOMMENDATIONS (1 of 3)
// ═══════════════════════════════════════════════════════════════════════
function coverageRecs1Page() {
  return [
    navyBanner('Coverage Recommendations'),
    leadPara('The following list identifies common coverage areas for your consideration. Please review with your NBAIS producer to determine which lines are recommended, currently insured, or not applicable to your operation.'),
    sectionDivider('Commercial Lines'),

    // Two-column layout via table
    new Table({
      width: { size: 100, type: WidthType.PERCENTAGE },
      rows: [
        recRow(
          ['Property Coverages', ['Building / Business Personal Property', 'Business Income & Extra Expense', 'Equipment Breakdown', 'Inland Marine / Contractors Equipment', 'Installation Floater', 'Builders Risk']],
          ['Automobile Coverage', ['Commercial Auto Liability', 'Physical Damage (Comp & Collision)', 'Hired & Non-Owned Auto', 'Motor Truck Cargo']]
        ),
        recRow(
          ['Liability Coverages', ['Commercial General Liability', 'Products & Completed Operations', 'Contractual Liability', 'Personal & Advertising Injury']],
          ["Workers' Compensation Coverages", ["Workers' Compensation — Statutory", "Employers' Liability", 'Stop Gap / Employers Liability (Monopolistic States)']]
        ),
        recRow(
          ['Cyber / Identity Theft / Crime', ['Cyber Liability', 'Data Breach / Privacy Liability', 'Identity Theft Protection', 'Commercial Crime / Employee Dishonesty']],
          ['Umbrella / Excess Liability', ['Commercial Umbrella', 'Excess Liability']]
        ),
        recRow(
          ['Directors & Officers / EPL / Fiduciary', ['Directors & Officers Liability', 'Employment Practices Liability (EPLI)', 'Fiduciary Liability']],
          ['Errors & Omissions / Professional', ['Professional Liability / E&O', 'Contractors Professional Liability', 'Design-Build Professional Liability']]
        ),
      ],
    }),
  ]
}

function recRow(leftSection, rightSection) {
  return new TableRow({
    children: [
      recCell(leftSection[0], leftSection[1]),
      recCell(rightSection[0], rightSection[1]),
    ],
  })
}

function recCell(heading, items) {
  return new TableCell({
    width: { size: 50, type: WidthType.PERCENTAGE },
    borders: { top: noBorder, bottom: noBorder, left: noBorder, right: noBorder },
    children: [
      new Paragraph({
        spacing: { before: 120, after: 60 },
        children: [new TextRun({ text: heading, font: FONT, size: 20, bold: true, color: BLUE })],
      }),
      ...items.map(item =>
        new Paragraph({
          bullet: { level: 0 },
          spacing: { before: 0, after: 20 },
          children: [new TextRun({ text: item, font: FONT, size: 18, color: BLACK })],
        })
      ),
    ],
  })
}

// ═══════════════════════════════════════════════════════════════════════
// PAGE 8 — COVERAGE RECOMMENDATIONS (2 of 3)
// ═══════════════════════════════════════════════════════════════════════
function coverageRecs2Page() {
  return [
    navyBannerContinued('Coverage Recommendations (continued)'),
    sectionDivider('Commercial Lines (continued)'),

    new Table({
      width: { size: 100, type: WidthType.PERCENTAGE },
      rows: [
        recRow(
          ['Wind / Hail, Earthquake, Flood', ['Wind & Hail Coverage', 'Earthquake Coverage', 'Flood Coverage (NFIP or Private)']],
          ['Pollution Liability', ['Contractors Pollution Liability', 'Environmental Impairment Liability', 'Site Pollution Legal Liability']]
        ),
        recRow(
          ['Foreign Coverages', ["Foreign Voluntary Workers' Compensation", 'Foreign General Liability', 'Foreign Auto', 'Foreign Package Policy']],
          [' ', []] // empty right cell
        ),
      ],
    }),

    sectionDivider('Personal Lines'),
    new Table({
      width: { size: 100, type: WidthType.PERCENTAGE },
      rows: [
        recRow(
          ['Personal Insurance', ['Automobile', 'Home / Homeowners', 'Flood / Earthquake', 'Personal Umbrella']],
          [' ', ['Farm & Ranch', 'Watercraft / Recreational Vehicles', 'Personal Articles Floater']]
        ),
      ],
    }),

    sectionDivider('Bond Recommendations'),
    new Table({
      width: { size: 100, type: WidthType.PERCENTAGE },
      rows: [
        recRow(
          ['Surety & Bonds', ['Contract Bond', 'Court Bond', 'Fidelity Bond', 'Financial Institution Bond']],
          [' ', ['License & Permit Bond', 'Probate Bond', 'Public Official Bond', 'Surety Bond']]
        ),
      ],
    }),
  ]
}

// ═══════════════════════════════════════════════════════════════════════
// PAGE 9 — EMPLOYEE BENEFITS RECOMMENDATIONS
// ═══════════════════════════════════════════════════════════════════════
function employeeBenefitsPage() {
  return [
    navyBanner('Employee Benefits Recommendations'),
    leadPara('Group benefits, life planning, and retirement plan services available through NBAIS for member consideration.'),

    sectionDivider('Group Benefits'),
    new Table({
      width: { size: 100, type: WidthType.PERCENTAGE },
      rows: [
        recRow(
          ['Health & Welfare', ['HR Services', 'Group Medical', 'Group Dental', 'Vision', 'Group Life and Accidental Death & Dismemberment (AD&D)']],
          ['Disability & Supplemental', ['Long Term Care', 'Short Term Disability', 'Section 125 Cafeteria Plans', 'Individual Medical / Dental']]
        ),
      ],
    }),

    sectionDivider('Life Department'),
    new Table({
      width: { size: 100, type: WidthType.PERCENTAGE },
      rows: [
        recRow(
          [' ', ['Business Planning', 'Estate Planning']],
          [' ', []]
        ),
      ],
    }),

    sectionDivider('Retirement Plan Services'),
    new Table({
      width: { size: 100, type: WidthType.PERCENTAGE },
      rows: [
        recRow(
          [' ', ['Qualified Plans', 'Non-Qualified Plans']],
          [' ', []]
        ),
      ],
    }),

    // Callout
    new Paragraph({
      shading: { type: ShadingType.CLEAR, fill: LT_BLUE },
      border: { left: { style: BorderStyle.SINGLE, size: 6, color: BLUE } },
      spacing: { before: 300, after: 100 },
      indent: { left: convertInchesToTwip(0.15) },
      children: [
        new TextRun({ text: 'Discuss with your producer. ', font: FONT, size: 18, bold: true, color: NAVY }),
        new TextRun({ text: 'Your NBAIS producer can help you assess which of these coverage lines apply to your operation and identify any potential gaps in your current insurance program.', font: FONT, size: 18, color: BLACK }),
      ],
    }),
  ]
}

// ═══════════════════════════════════════════════════════════════════════
// BUILD DOCUMENT
// ═══════════════════════════════════════════════════════════════════════
async function buildDocument() {
  const sectionProps = {
    page: {
      size: {
        width: convertInchesToTwip(8.5),
        height: convertInchesToTwip(11),
      },
      margin: {
        top: convertInchesToTwip(0.5),
        bottom: convertInchesToTwip(0.5),
        left: convertInchesToTwip(0.55),
        right: convertInchesToTwip(0.55),
      },
    },
  }

  const interiorSectionProps = {
    ...sectionProps,
    headers: { default: interiorHeader() },
    footers: { default: interiorFooter() },
  }

  const doc = new Document({
    styles: {
      default: {
        document: {
          run: {
            font: FONT,
            size: BODY_SIZE,
            color: BLACK,
          },
          paragraph: {
            spacing: { line: 290 }, // ~1.45 line height
          },
        },
      },
      paragraphStyles: [
        {
          id: 'ListBullet',
          name: 'List Bullet',
          basedOn: 'Normal',
          run: { font: FONT, size: SMALL_SIZE },
        },
      ],
    },
    sections: [
      // Page 1 — Cover (no header/footer)
      {
        properties: sectionProps,
        children: coverPage(),
      },
      // Page 2 — Cover Letter
      {
        properties: interiorSectionProps,
        children: coverLetterPage(),
      },
      // Page 3 — Premium Summary
      {
        properties: interiorSectionProps,
        children: premiumSummaryPage(),
      },
      // Page 4 — Coverage Details 1
      {
        properties: interiorSectionProps,
        children: coverageDetails1Page(),
      },
      // Page 5 — Coverage Details 2
      {
        properties: interiorSectionProps,
        children: coverageDetails2Page(),
      },
      // Page 6 — Next Steps + Auth
      {
        properties: interiorSectionProps,
        children: nextStepsPage(),
      },
      // Page 7 — Coverage Recs 1
      {
        properties: interiorSectionProps,
        children: coverageRecs1Page(),
      },
      // Page 8 — Coverage Recs 2
      {
        properties: interiorSectionProps,
        children: coverageRecs2Page(),
      },
      // Page 9 — Employee Benefits
      {
        properties: interiorSectionProps,
        children: employeeBenefitsPage(),
      },
    ],
  })

  const buffer = await Packer.toBuffer(doc)
  const outPath = join(OUTDIR, 'master.docx')
  writeFileSync(outPath, buffer)
  console.log(`✓ Generated ${outPath} (${(buffer.length / 1024).toFixed(1)} KB)`)
}

buildDocument().catch(err => {
  console.error('Build failed:', err)
  process.exit(1)
})
