# Review Report — ADO#2593

**Reviewer:** Hawkeye (Clint Barton)
**Commits:** d6e2327 + da247a0
**Date:** 2026-04-30
**Cycle:** 1

---

### Verdict: NEEDS-CHANGES

One confirmed critical bug (F1 — EL fee constant $120 vs spec $20). All structural, template, and routing checks clear. Fix F1 and it ships.

---

### CC Review

**Command used:**
```bash
cat /home/fredw/projects/fip/services/proposal-generator/pipeline/ADO2593-REVIEW-BRIEF.md | claude --model sonnet --print --dangerously-skip-permissions
```

CC read all 6 changed files plus the extracted `master.docx` XML. CC confirmed F1 as the only critical bug, cleared F2/F3/F4/F5, passed all structural checks A–F, and flagged G (no S3 sync in build script) as a process risk.

---

### Spec Compliance Check

**WI:** ADO#2593 (NBAIS WC proposal template — commits d6e2327 + da247a0)

**Files modified:**
- `scripts/build-nbais-wc-template.py` — ✅ created as required
- `templates/verticals/nbais-wc/master.docx` — ✅ created as required
- `templates/verticals/nbais-wc/meta.json` — ✅ created as required
- `src/services/assembleTemplateData.js` — ✅ modified as required
- `src/services/documentRenderer.js` — ✅ modified as required
- `test-payloads/nbais-wc-test.json` — ✅ created as required

**Spec compliance verdict:** ⚠️ NON-COMPLIANT on one data point (F1 EL fee value) — blocks PASS

---

### Consistency Audit

| Cross-check | Result |
|---|---|
| Template `{#classSchedule}` ↔ Assembler `classSchedule` key | ✅ Match |
| Template `{#excludedPersons}/{name}` ↔ Assembler `{ name: ... }` object | ✅ Match |
| Template `{@stackedLogoBase64}` ↔ ImageModule `@` prefix | ✅ Match |
| `meta.json` logos config ↔ `loadNamedLogos()` key iteration | ✅ Match |
| `meta.vertical = 'nbais-wc'` ↔ `isNbaisWc` check in renderer | ✅ Match |
| `assembleNbaisWcTemplateData` export ↔ import in documentRenderer | ✅ Match |

---

### Critical Issues [1]

#### C1: EL fee constant is $120 — spec requires $20
- **File:** `src/services/assembleTemplateData.js`, line 165
- **Category:** correctness / spec non-compliance
- **Issue:** `const elFeeNum = 120` — BAWNSIG program constant should be $20 per WI Comment 1 ("employersLiabilityFee = 20 (constant)"). Tony's code and JSDoc both document it as $120 — consistently wrong.
- **Impact:** Every generated WC proposal overstates `employersLiabilityFee` by $100, overstates `totalEstimatedPremium` by $100, and overstates `downPayment` by $25 (25% × $100). Financial figures presented to members are incorrect.
- **Fix:**
  ```diff
  - const elFeeNum = 120  // BAWNSIG program constant — $120 EL fee
  + const elFeeNum = 20   // BAWNSIG program constant — $20 EL fee
  ```
  Also fix the JSDoc at line ~121:
  ```diff
  - *   employersLiabilityFee ← CONSTANT: $120 — formatted currency
  + *   employersLiabilityFee ← CONSTANT: $20 — formatted currency
  ```

---

### Pre-flagged Issues — Full Disposition

| Flag | Status | Notes |
|---|---|---|
| **F1** EL fee `elFeeNum = 120` | ❌ CONFIRMED BUG | Wrong constant. Fix: `const elFeeNum = 20`. See C1 above. |
| **F2** `{#classSchedule}` vs spec `{#wcClasses}` | ✅ CLEARED | Template and assembler both use `classSchedule` — internally consistent. WI spec naming deviation has no runtime effect. |
| **F3** `{name}` vs spec `{excludedPersonName}` | ✅ CLEARED | Template, assembler, and test payload all use `name` key — end-to-end consistent. Spec naming deviation, not a bug. |
| **F4** Header logo static vs dynamic | ✅ CLEARED | Static baked-in logo is intentional architecture. Build script embeds `logo_horizontal.png` at template creation time via `run_logo.add_picture()`. No dynamic injection needed. **Observation (non-blocking):** `horizontalLogoBase64` is assembled but never used by any template tag — dead code. Recommend removing or documenting. |
| **F5** Sections 3–9 have no headerReference | ✅ CLEARED | Build script calls `link_header(s3–s9)` → `section.header.is_linked_to_previous = True`. OOXML behavior: sections without explicit headerReference inherit previous section's header (header2.xml — logo + rule). All interior pages will display the correct header. |

---

### Additional Checks

| Check | Result | Notes |
|---|---|---|
| **A** Real Word headers (not content-area tables) | ✅ PASS | `header1.xml` (blank cover header) and `header2.xml` (logo + italic doc-tag + 2pt navy bottom border) confirmed as proper OOXML header parts. |
| **B** Cover page isolation | ✅ PASS | Section 1 has `<w:titlePg/>` + `headerReference type="first"` → blank header1.xml. Zero-margin cover, no header/footer displayed on cover page. |
| **C** Page structure: 9 sectPr blocks | ✅ PASS | 9 sections = 9 pages: Cover, TOC, Cover Letter, Premium Summary, Coverage Details (cont.), Next Steps & Auth, Recs p1, Recs p2, Employee Benefits. WI Comment 767548 lists exactly 9 pages — supersedes the "8 pages" count from earlier Comment 767517. Tony's build report also states "9 unique section footers." |
| **D** Image tag prefix `{@}` vs `{%}` | ✅ PASS | `docxtemplater-image-module-free` uses `@` prefix by default. Build script emits `{@stackedLogoBase64}`. ImageModule in documentRenderer.js configured correctly. |
| **E** Test payload validity | ✅ PASS | All required fields present: `templateId`, `lineOfBusiness: "WorkersCompensation"`, `premium`, `scheduleItems` (2 × `employee_class`), `nbaisWc.excludedPersons` (2 × `{name}`), `policyPeriod` dates. No structural violations. |
| **F** nbais-wc routing in documentRenderer | ✅ PASS | `isNbaisWc = meta.vertical === 'nbais-wc'` gates `assembleNbaisWcTemplateData` call. Import confirmed. Routing is in documentRenderer.js as specified. |
| **G** S3 sync in build script | ⚠️ PROCESS RISK | Build script (`build-nbais-wc-template.py`) ends at `doc.save(OUTPATH)` — no `aws s3 sync` step. Tony sync'd manually (confirmed in build report). If template is rebuilt and someone forgets to sync, the service pulls a stale `master.docx` from S3. Non-blocking for this cycle, but recommend adding sync to build script or documenting the deployment step. |

---

### Positive Observations

- Real Word header/footer architecture is exactly right. Previously builds used content-area fake headers — this is the corrected pattern going forward.
- `loadNamedLogos()` is a clean addition — graceful null handling, iterates meta.logos config, no hardcoded filenames.
- Cover page zero-margin + titlePg isolation is properly handled. The `link_header()` pattern in the build script is the correct way to express "same header as previous" in python-docx.
- 9-section/9-footer structure with unique footer labels per section is solid — easy to maintain and extend.
- Test payload covers all dynamic fields including 2 class codes and 2 excluded persons — good coverage for template loop testing.

---

### What Tony needs to fix

**Required before merge:**

1. `src/services/assembleTemplateData.js` line 165:
   ```js
   const elFeeNum = 20   // BAWNSIG program constant — $20 EL fee
   ```
2. JSDoc comment at line ~121 — update `$120` → `$20`

That's it. Two-line fix.

**Recommended (non-blocking, can be follow-up tickets):**
- Remove or document `horizontalLogoBase64` in `assembleNbaisWcTemplateData` return — it's dead code (F4 observation)
- Add S3 sync step to `build-nbais-wc-template.py` or add a deployment README note (Check G)

---

## Cycle 2 Review

**Reviewer:** Hawkeye (Clint Barton)
**Commit:** 515c39d
**Date:** 2026-04-30
**Cycle:** 2

### Verdict: PASS ✅

### CC Verification

CC confirmed:
1. `elFeeNum = 20` at line 165 — ✅ correct
2. JSDoc comment updated to `$20` at line 120 — ✅ correct
3. No other lines changed — ✅ diff is exactly 2 lines

### Commit Scope

`git show 515c39d --stat` confirms:
- **1 file changed** — `services/proposal-generator/src/services/assembleTemplateData.js`
- **2 insertions, 2 deletions** — exactly the two-line fix described

No unintended changes. No scope creep.

### Final Status

All C1 findings from Cycle 1 resolved. No new issues introduced. Ready for deploy.
