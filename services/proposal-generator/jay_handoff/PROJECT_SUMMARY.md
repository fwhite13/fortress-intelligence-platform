# NBAIS WC Proposal Project — Summary & Handoff

**Project owner:** Caleb (NBAIS / FAM Operations)
**Last updated:** 2026-04-30
**Purpose:** Single source of truth describing the NBAIS Workers' Compensation member proposal — content, structure, design system, and production pipeline. If continuing this project in a new chat or with a new AI, start here.

---

## 1. What this project is

NBAIS (Nevada Builders Alliance Insurance Solutions) is a workers' compensation program for Nevada construction industry members. The carrier is BAWNSIG (Builders Association of Western Nevada Self-Insured Group), administered by program manager Lusense. The program is delivered to members via Higginbotham.

This project produces the **member-facing proposal document** that goes out per member after a carrier quote is generated. The document presents WC coverage, premium, coverage details, a binding signature page, and a list of additional coverage recommendations.

The proposal must be:
- **Per-member personalized** — populated from data scraped from the BAWNSIG carrier quote
- **Bindable** — includes a signature page authorizing coverage
- **Brand-consistent** — NBAIS visual identity throughout
- **Production-ready** — generated automatically by an AI proposal generator, not assembled manually

---

## 2. Final architecture decision

After exploring multiple approaches (split static/dynamic Word docs, combined Word doc, etc.), the final approach is:

**HTML/CSS template → rendered to PDF for member delivery.**

Rationale:
- HTML/CSS gives precise visual control that Word cannot
- The AI proposal generator does string replacement on merge field tokens
- The rendering tool (WeasyPrint, Puppeteer, or whatever the engineer chooses) outputs a print-quality PDF
- Members never see HTML — they see a polished PDF

Deliverables sit in two buckets:
- **For Caleb's stakeholder handoff:** a sample populated PDF showing how it'll look
- **For the AI engineer:** zip bundle with `proposal.html`, `styles.css`, both logo PNGs, and `SPEC.md`

---

## 3. Page order (FINAL)

1. Cover Page
2. Cover Letter
3. Premium Summary
4. Coverage Details (1 of 2)
5. Coverage Details (2 of 2)
6. **Next Steps + Member Signature Page** — bindable
7. Coverage Recommendations (1 of 3) — Commercial Lines part 1
8. Coverage Recommendations (2 of 3) — Commercial Lines part 2 + Personal Lines + Bonds
9. Employee Benefits Recommendations (3 of 3)

**Removed from earlier drafts:** Market Response, Executive Summary, About NBAIS page, separate "What you'll receive" page.

**Why this order:** The signature is tied to the WC proposal and binding. Recommendations come after as an informational leave-behind, separate from what the member signed.

---

## 4. Signature page — finalized

The signature page (page 6) is the bindable acceptance for the WC coverage above it.

**Authorization language (draft pending Caleb's confirmation):**

> By signing below, the undersigned acknowledges receipt of this Workers' Compensation Insurance proposal and authorizes Nevada Builders Alliance Insurance Services (NBAIS) to bind coverage as described herein, effective on the policy period stated above. The undersigned confirms that the payroll, classification codes, and excluded persons listed in this proposal are accurate to the best of their knowledge and understands that final premium is subject to audit. The required initial down payment will be remitted online via the secure payment link provided upon binding.

**Signature block — exactly four lines, no more, no less:**

- By: ________________________
- Print Name: ________________________
- Title: ________________________
- Date: ________________________

Member signature only. No producer signature. No additional fields (no FEIN, no witness, no notary).

---

## 5. Branding

### Colors
- Navy: `#1F3864` — primary, banners, table headers, h2/h3 text
- Blue: `#2E75B6` — secondary, accent borders, h3 subheads
- Light Blue: `#EBF3FB` — highlight rows, callout backgrounds
- Light Gray: `#F5F5F5` — alternating table rows, contact blocks
- Mid Gray: `#CCCCCC` — borders
- Dark Gray: `#595959` — body secondary text, footers
- Red: `#C00000` — merge field tokens only

### Typography
- Family: Helvetica Neue / Helvetica / Arial
- Body: 10pt, line-height 1.45
- h1 (cover title): 30pt bold
- h2 (banner): 14pt bold
- h3 (subhead): 11pt bold

### Logos (provided by Caleb)
- `logo_horizontal.png` — used in page header on every interior page (right-aligned to a doc-tag label, with navy rule below)
- `logo_stacked.png` — used on the cover page (centered, large)

### Page format
- US Letter (8.5" × 11")
- Internal padding: 0.5"–0.55" all sides
- Header: navy 2pt rule below logo + "Workers' Compensation Proposal" italic gray label
- Footer: 1pt mid-gray rule above; left says `NBAIS Workers' Compensation Proposal · {{MEMBER_NAME}} · Confidential`; right says page label

---

## 6. Merge fields — complete list

All fields use `{{TOKEN}}` syntax in HTML, wrapped in `<span class="mf">` for visual distinction during template review.

### Member identity
- `{{MEMBER_NAME}}` — entity name, used on cover, cover letter, every footer
- `{{MEMBER_ADDRESS}}` — full address block on cover letter
- `{{MEMBER_LEGAL_NAME}}` — legal entity name, Coverage Details "Named Insured"

### Policy
- `{{POLICY_PERIOD}}` — e.g., `4/24/2026 – 12/31/2026`
- `{{QUOTE_DATE}}` — quote date from carrier doc

### Class schedule (one row per class code; duplicate row for multiples)
- `{{CLASS_CODE}}` — e.g., `6217`
- `{{CLASS_DESCRIPTION}}` — e.g., `Excavation & Drivers`
- `{{EST_ANNUAL_PAYROLL}}` — e.g., `$144,000`
- `{{RATE}}` — e.g., `$4.995`
- `{{EST_PREMIUM}}` — e.g., `$7,192.80`

### Charges
- `{{SURPLUS_CONTRIBUTION}}` — required SIG contribution at 8%
- `{{EMPLOYERS_LIABILITY_FEE}}` — typically `$120.00`
- `{{TOTAL_ESTIMATED_PREMIUM}}` — sum of all charges
- `{{DOWN_PAYMENT}}` — 25% of total, new business

### Excluded persons (conditional — see §8)
- `{{EXCLUDED_PERSON_1}}`
- `{{EXCLUDED_PERSON_2}}`

### Hardcoded (not merge fields, but flagged for future update)
- Producer: Dianne Slater (hardcoded in template — title, phone, email, office address, website are bracketed `[placeholders]` until Caleb confirms)

---

## 7. Page-by-page content reference

### Page 1 — Cover
- Top navy rule + "Nevada Builders Alliance Insurance Solutions" eyebrow
- Stacked logo (centered, ~2.6")
- Title: "Workers' Compensation Insurance Proposal"
- Subtitle: "Prepared exclusively for Nevada Builders Alliance members"
- Meta grid: Prepared For, Policy Period, Prepared By (Dianne Slater), Date, Program
- Bottom navy rule + "Confidential — Prepared for the named member's exclusive use"

### Page 2 — Cover Letter
- Date / member name / member address (merge fields)
- "RE:" line
- Salutation: "Dear `{{MEMBER_NAME}}`,"
- "About this proposal" — two paragraphs explaining NBAIS and the program
- "Program highlights" — light-blue bordered box with 5 checkmark bullets
- "What is included in this proposal" — bulleted list of the four sections

### Page 3 — Premium Summary
- Banner: "Premium Summary"
- Lead: "Your estimated cost for the coverage period `{{POLICY_PERIOD}}`. All figures are subject to final payroll audit."
- "Coverage at a Glance" table — full label/value list with merge fields, total row in light blue highlight
- "What's next" subhead with one paragraph

### Page 4 — Coverage Details (1 of 2)
- Banner: "Coverage Details — Workers' Compensation"
- "Policy Information" — KV table (Carrier, Program Manager, Financial Strength disclosure note, Policy Period, Coverage, States Covered = Nevada)
- "Named Insured" — merge field
- "Coverage and Limits" — table with Part I (Statutory) and Part II EL limits ($1M / $1M / $1M)
- "Employee Classification Schedule" — table with state, class code, description, payroll, rate, premium, plus total row

### Page 5 — Coverage Details (2 of 2)
- Banner: "Coverage Details (continued)"
- "Surplus Contribution" — paragraph explaining the 8% SIG contribution
- "Excluded Persons" — table (conditional — see §8)
- D-43 form requirement note + "Important" note
- "Self-Insured Group Disclosure" — full text in a light-gray callout

### Page 6 — Next Steps + Signature
- Banner: "Next Steps"
- Action paragraph: review, confirm payroll/class codes, contact producer
- Two-column contact grid: NBAIS Producer (Dianne) + NBAIS Program Office
- Authorization paragraph (see §4)
- Signature block: By / Print Name / Title / Date
- Closing disclaimer (fine print)

### Page 7 — Coverage Recommendations (1 of 3)
- Banner: "Coverage Recommendations"
- Lead paragraph
- Section divider: "Commercial Lines"
- Two-column layout, 8 sections:
  - Property Coverages
  - Liability Coverages
  - Cyber / Identity Theft / Crime
  - Automobile Coverage
  - Workers' Compensation Coverages
  - Umbrella / Excess Liability
  - Directors & Officers / EPL / Fiduciary
  - Errors & Omissions / Professional

### Page 8 — Coverage Recommendations (2 of 3)
- Banner: "Coverage Recommendations (continued)"
- Section divider: "Commercial Lines (continued)" — Wind/Hail/Earthquake/Flood, Foreign, Pollution
- Section divider: "Personal Lines" — Auto, Home, Flood, Umbrella, Farm & Ranch, Watercraft, Articles Floater
- Section divider: "Bond Recommendations" — Contract, Court, Fidelity, Financial Institution, License & Permit, Probate, Public Official, Surety

### Page 9 — Employee Benefits (3 of 3)
- Banner: "Employee Benefits Recommendations"
- Lead paragraph
- Section divider: "Group Benefits" — HR Services, Group Medical/Dental/Vision/Life/AD&D, LTC, STD, Section 125, Individual Med/Dental
- Section divider: "Life Department" — Business Planning, Estate Planning
- Section divider: "Retirement Plan Services" — Qualified Plans, Non-Qualified Plans
- Closing callout: "Discuss with your producer"

---

## 8. Conditional logic

### Excluded Persons block (Page 5)
Behavior depends on what the carrier quote contains:

- **Zero excluded persons** → remove the entire "Excluded Persons" h3 + table + the two notes that follow. Keep Surplus Contribution and SIG Disclosure.
- **One excluded person** → populate `{{EXCLUDED_PERSON_1}}`, remove the second `<tr>`.
- **Two excluded persons** → populate both, no structural changes.
- **Three or more** → duplicate the row pattern as needed.

### Multiple class codes (Page 4)
The Classification Schedule currently has one row. If the quote has multiple class codes:
- Duplicate the data row pattern for each
- Update the "Total Estimated Premium" footer row to sum all rows
- The `{{EST_PREMIUM}}` field on Page 3 should reflect the total

### Page break handling (open question)
Tables that may expand (Class Schedule, Excluded Persons) are positioned to accommodate growth without requiring complex pagination logic. Caleb's preferred approach: tables go at the top of their pages so they grow downward naturally and push subsequent content. The engineer should confirm their renderer handles row-level page breaks (most do — `page-break-inside: avoid` on `<tr>` and repeating `<thead>` on continuation pages).

---

## 9. Key constraints — DO NOT change without Caleb's approval

- **Section order is final** (see §3)
- **Banner colors:** navy `#1F3864` only
- **Logo placement:** stacked on cover, horizontal in headers
- **Page count:** 9 pages base, may extend with conditional content
- **Output format:** PDF (never HTML, never Word for member delivery)
- **No condensing or shrinking** — content stays at full size
- **Merge field syntax:** `{{TOKEN_NAME}}` — do not change names
- **Producer:** Dianne Slater is hardcoded (until Caleb says otherwise)

---

## 10. Deferred — DO NOT add without explicit direction

- Experience Modification Rate (Emod)
- Endorsements schedule
- Audit notes
- Market Response section
- Executive Summary section
- Service Team page
- About NBAIS page (removed in final cut)
- Marketing-quality enhancements (dividend history, competitor comparison, savings callouts)

---

## 11. Files in this project

### For Caleb's stakeholder handoff
- `NBAIS_WC_Proposal.pdf` — sample populated proposal showing visual design

### For the AI engineer (zip bundle)
- `proposal.html` — master template
- `styles.css` — full stylesheet
- `logo_horizontal.png` — header logo
- `logo_stacked.png` — cover logo
- `SPEC.md` — engineer specification covering pipeline, merge fields, conditionals, validation

### Supporting reference documents
- `NBAIS_SOP.docx` — operational SOP for the NBAIS WC program (separate from this proposal)
- `PROJECT_SUMMARY.md` — this document

---

## 12. Project context — non-technical

- **Caleb's role:** runs the NBAIS launch within FAM Operations
- **Direct teammates:** Rob, Lauren, Lynn, Steve, Elise, Amanda, Diane, Pam
- **Current top priority:** NBAIS platform launch
- **Producer named in proposal:** Dianne Slater (note: spelled "Dianne" not "Diane")
- **Embedded Resource (ER):** Dianne Slater is the ER for NBAIS members; she's the single point of contact for every member
- **Power Office (PO):** handles non-WC lines via Higginbotham's Epic system; not involved in this proposal
- **Tooling on Caleb's side:** Claude (this chat) for project management/decisions, Copilot for email triage, Fate for meeting intelligence
- **Sample carrier quote used during development:** BAWNSIG quote dated 4/27/2026 — actual member name redacted; example data uses placeholder excluded persons "James Moretti" and "Sandra Cole"

---

## 13. If you're a new AI picking this up

The next thing to build (as of this checkpoint) is:

1. Confirm authorization language in §4 with Caleb
2. Rebuild the HTML/CSS proposal with the FINAL page order (§3)
3. Add the signature page (§4) replacing the previous Next Steps page
4. Remove the About NBAIS page entirely
5. Position the Class Schedule and Excluded Persons tables at the top of their pages (Caleb's directive — see §8 page break note)
6. Re-render the PDF for stakeholder handoff
7. Update `SPEC.md` to reflect all of the above
8. Re-bundle the engineer zip

Do not ask Caleb to re-explain anything in this document. Treat it as authoritative unless he says otherwise.
