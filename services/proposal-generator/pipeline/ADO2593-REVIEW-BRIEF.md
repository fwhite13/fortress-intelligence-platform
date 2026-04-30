# ADO#2593 — CC Review Brief (Hawkeye, Cycle 1)

You are performing an adversarial code review for ADO#2593 (NBAIS WC proposal template).
Review commit d6e2327 (+ da247a0). Work in: /home/fredw/projects/fip/services/proposal-generator/

## CONTEXT — What was built

A new NBAIS Workers' Compensation proposal vertical for the proposal-generator service:
- `scripts/build-nbais-wc-template.py` — python-docx builder (1,518 lines)
- `templates/verticals/nbais-wc/master.docx` — 9-section Word template
- `templates/verticals/nbais-wc/meta.json` — template metadata
- `src/services/assembleTemplateData.js` — new `assembleNbaisWcTemplateData` function added
- `src/services/documentRenderer.js` — `loadNamedLogos` + `isNbaisWc` branch added
- `test-payloads/nbais-wc-test.json` — test payload

## WI SPEC — Key requirements (from ADO comments):

**Comment 1 (data sourcing):** 
- surplusContribution = basePremium * 0.08 (COMPUTED)
- employersLiabilityFee = 20 (CONSTANT — "likely a fixed program constant (20)")
- downPayment = totalEstimatedPremium * 0.25 (COMPUTED)

**Comment 3 (Jay revision):**
- Page structure: Cover, TOC, Cover Letter, Premium+Coverage consolidated (1 page), Dynamic tables, Authorization, Recs p1, Recs p2 = 8 pages total

**Comment 4 (critical authoring):**
- Headers/footers MUST be real Word headers — not content-area tables
- Cover page (pg 1): NO header/footer — titlePg / section break isolates it
- Interior pages (2-8): header has logo_horizontal.png + italic doc-tag + 2pt navy bottom rule

## PRE-FLAGGED ISSUES — Evaluate each:

### F1 — EL fee constant
File: `src/services/assembleTemplateData.js`
Look for: `elFeeNum = 120`
WI Comment 1 says: employersLiabilityFee = 20 (constant)
Tony's build report says: "$120 EL fee" and comment says "$120 constant"
QUESTION: Is 120 correct or wrong? WI Comment 1 says 20. Tony hardcoded 120.
This is a math/data error — the WI spec says $20, the code says $120.
VERDICT NEEDED: Confirm F1 is a CRITICAL bug (wrong constant).

### F2 — Loop tag name: {#classSchedule} vs {#wcClasses}
File: extracted master.docx (word/document.xml) AND assembleTemplateData.js
The assembler outputs key: `classSchedule`
The template uses: `{#classSchedule}` / `{/classSchedule}` 
WI Comment 4 says the spec uses `{#wcClasses}` 
QUESTION: Is this internally consistent (assembler + template both use classSchedule)? 
If so, is the spec deviation a blocker? The template and assembler agree — they just don't match the WI spec tag name.
Read both files and confirm they agree on `classSchedule`.

### F3 — Excluded person inner tag: {name} vs {excludedPersonName}
File: document.xml AND assembleTemplateData.js  
Assembler outputs `excludedPersons` array with objects having key `name`
Template uses `{#excludedPersons}` ... `{name}` ... `{/excludedPersons}`
WI comment mentions `{excludedPersonName}` as the inner tag name
QUESTION: Are template and assembler consistent with each other (both use `name`)?
If so, is this a blocker vs spec or just a naming convention deviation?

### F4 — Header logo: static baked-in image vs dynamic docxtemplater tag
File: templates/verticals/nbais-wc/master.docx (word/header2.xml)
CONFIRMED from XML analysis: header2.xml contains a `<w:drawing>` with `r:embed="rId1"` pointing to media/image1.png (a 1600x485 PNG — the horizontal logo).
This is a STATIC baked-in image in the header — NOT a docxtemplater image tag like {%horizontalLogoBase64}.
The assembler DOES output `horizontalLogoBase64` as a base64 string, but header2.xml never uses a docxtemplater tag to render it — it uses a pre-embedded PNG.
QUESTION: Is this a bug? Will the logo in the header always be the static logo baked at template-build time, rather than dynamic from S3?
If the template is rebuilt from build-nbais-wc-template.py and pushed to S3 each time with the correct logo embedded, is this acceptable?
Check build-nbais-wc-template.py to see if it embeds the actual logo from jay_handoff/ at build time.

### F5 — Sections 3-9 have no headerReference
From document.xml analysis:
- Section 1: headerReference type="first" → rId9 → header1.xml (blank first-page header) + titlePg present
- Section 2: headerReference type="default" → rId11 → header2.xml (logo + doc-tag + navy rule)
- Sections 3-9: NO headerReference in their sectPr — they only have footerReference
QUESTION: In Word/OOXML, when a section has no headerReference, does it inherit/link to the previous section's header? Is this correct python-docx behavior meaning "same header as previous"?
This is the "link to previous" behavior in Word. Verify this is correct and that all interior pages (sections 2-9) will display header2.xml.

## ADDITIONAL CHECKS

### A. Real Word headers confirmation
CONFIRMED from extraction: word/header1.xml, word/header2.xml exist (real Word headers, not content tables).
header1.xml = blank (empty paragraph with Header style = correct for cover)
header2.xml = logo drawing + tab + italic "Workers' Compensation Proposal" + 2pt navy bottom border
✅ Real Word headers confirmed. Verify this is consistent with the WI requirement.

### B. Cover page isolation
CONFIRMED: Section 1 sectPr has:
- `<w:titlePg/>` — enables different first-page header
- `headerReference type="first"` → header1.xml (blank)
- `footerReference type="first"` → footer1.xml
- No `<w:type>` element = implicit nextPage break (Word default for sections without explicit type = nextPage)
✅ Cover page is isolated. Verify this meets spec.

### C. Page structure / 9 sectPr blocks vs 8 pages
The template has 9 sectPr blocks. WI Comment 3 says 8 pages.
QUESTION: Is 9 sectPr = 9 pages, or does the final sectPr not add a page?
In OOXML, the body's final `<w:sectPr>` (as a child of `<w:body>`, not inside a paragraph) defines the last section/page without adding an extra break. So 9 sectPr blocks = 9 pages.
WI Comment 1 (Tony's build report — Comment 767637) says "9 unique section footers" and the TOC page was added per Comment 767517 guidance.
WI Comment 767548 says total should be 9 pages (Cover, TOC, Cover Letter, Premium+Coverage, Dynamic tables, Next Steps+Auth, Recs p1, Recs p2, Employee Benefits Recs).
Count: 1+1+1+1+1+1+1+1+1 = 9. 
QUESTION: Check WI Comment 767548 carefully — it actually lists 9 pages (not 8). Is the 8-page statement from Comment 767517 superseded by 767548?

### D. Image tag syntax
The service uses `docxtemplater-image-module-free` which supports `{%tagName}` (% prefix).
Check documentRenderer.js ImageModule config — does it use `%` prefix module or `@` prefix?
Check document.xml for `{@stackedLogoBase64}` vs `{%stackedLogoBase64}`.
FOUND in document.xml: `{@stackedLogoBase64}` (@ prefix used in template)
FOUND in documentRenderer.js: ImageModule is instantiated but getImage/getSize are defined.
QUESTION: Does `docxtemplater-image-module-free` use `@` or `%` for image tags? Verify the prefix matches what the module expects.

### E. Test payload validity
Read `test-payloads/nbais-wc-test.json` and verify:
- templateId = "nbais-wc" ✅
- quotes[0].lineOfBusiness = "WorkersCompensation" ✅  
- quotes[0].premium = 14850.00 ✅
- scheduleItems present with employee_class items ✅
- nbaisWc.excludedPersons array present ✅
- policyPeriod.effectiveDate/expirationDate present ✅
Payload looks structurally valid. Confirm no schema violations.

### F. assembleTemplateData routing
CONFIRMED from documentRenderer.js:
- `isNbaisWc = meta.vertical === 'nbais-wc'`
- If isNbaisWc: calls `assembleNbaisWcTemplateData(payload, meta, namedLogos, logger)`
- Else: calls `assembleTemplateData(payload, meta, logoBuffer, ...)`
- `assembleNbaisWcTemplateData` is exported from assembleTemplateData.js ✅
The routing is in documentRenderer.js (not in assembleTemplateData main function). ✅

### G. S3 sync
Tony's build report (Comment 767637) states:
"S3 sync complete: s3://fortress-tools/fip-proposal-templates/verticals/nbais-wc/ (master.docx, meta.json, logo_horizontal.png, logo_stacked.png)"
Verify build script has an S3 sync step. Read scripts/build-nbais-wc-template.py and find the AWS CLI sync command.

## SPECIFIC FILES TO READ

1. Read `src/services/assembleTemplateData.js` — focus on `assembleNbaisWcTemplateData` function
   - Verify elFeeNum value (120 vs 20)
   - Verify classSchedule key name
   - Verify excludedPersons structure (name key)
   
2. Read `src/services/documentRenderer.js` — focus on:
   - `loadNamedLogos` function 
   - `isNbaisWc` branch logic
   - ImageModule instantiation — verify `@` vs `%` prefix

3. Read `scripts/build-nbais-wc-template.py` — focus on:
   - How horizontal logo is embedded in header (baked vs tag?)
   - S3 sync command at end of script
   - Section structure (count pages)

4. Read `templates/verticals/nbais-wc/meta.json` — verify logos config

5. Read `test-payloads/nbais-wc-test.json` — structural validity

## VERDICT CRITERIA

PASS (with F1 as NEEDS-CHANGES): If F1 is confirmed as the only real bug (120 vs 20), and F2/F3/F4/F5 are acceptable (internally consistent or structurally sound).

NEEDS-CHANGES: If F1 confirmed wrong, plus any of F2/F3/F4/F5 are real bugs.

FAIL: If structural problems found (e.g., header is fake content-area table — CONFIRMED NOT the case, headers are real Word XML headers).

## OUTPUT FORMAT

For each flagged issue F1-F5, provide:
- CONFIRMED BUG / CLEARED / NEEDS-ATTENTION
- Evidence (file, line, value)
- Severity

For additional checks A-G:
- PASS / ISSUE / NEEDS-ATTENTION  
- Brief explanation

Overall verdict: PASS (NEEDS-CHANGES for F1) / NEEDS-CHANGES / FAIL

---
## Cycle 2 Fix
- assembleTemplateData.js line 165: elFeeNum 120 -> 20 (per WI Comment 2)
- No other changes
