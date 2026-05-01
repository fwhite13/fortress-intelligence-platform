# Build Report: ADO#2604 — NBAIS WC Visual Polish

**Commit:** 1db791c
**Date:** 2026-04-30

## Changes Made

### scripts/build-nbais-wc-template.py

- **Fix 1 — PAGE SIZE [BLOCKER]:** Root cause identified: `Emu(PAGE_W * 914)` used wrong twip→EMU conversion factor (914 instead of 635), producing A3-sized pages (12.2×15.8in). Fixed by replacing all page size assignments with `Inches(8.5)` / `Inches(11)` in both the Section 1 block and `apply_standard_margins()`. Updated `CONTENT_W` constant from 10656 to 9360 twips (6.5in for 1-inch margins). All 8 sections now produce `<w:pgSz w:w="12240" w:h="15840"/>` — verified via docx XML inspection.

- **Fix 2 — LOGO TAG [BLOCKER]:** `{@stackedLogoBase64}` → `{%stackedLogoBase64}`. docxtemplater-image-module-free requires `{%tag}` prefix (confirmed from package README). The `{@tag}` syntax was unrecognized, causing blank logo on cover.

- **Fix 3 — COVER TITLE:** Changed "Insurance Proposal" → "Workers' Compensation Insurance Proposal" (26pt bold navy). Font size reduced from 30pt to 26pt to prevent line overflow.

- **Fix 4 — BANNER HEIGHT [HIGH]:** `add_banner()` `space_before`/`space_after` changed from `Pt(0)` → `Pt(8)` for ~10-12pt top/bottom padding. Applies to all banners (14 occurrences via single function).

- **Fix 5 — CELL PADDING [HIGH]:** `set_cell_margins()` defaults updated from `top=60, bottom=60, left=80, right=80` → `top=80, bottom=80, left=115, right=115`. All explicit low-value margin calls in `add_kv_table()`, `build_premium_summary_page()`, and `build_coverage_details_continued_page()` updated to match.

- **Fix 6 — COLUMN RATIO [HIGH]:** `build_premium_summary_page()` label ratio changed `0.38` → `0.35`. With CONTENT_W=9360 at fix 1: label=3276 twips (2.275in), value=6084 twips (4.225in) ≈ 35/65 split. `add_kv_table()` already used `label_pct=35` default.

- **Fix 7 — TOTAL ROW COLOR:** Already correct — `color=NAVY, bold=True`. No change needed.

- **Fix 8 — WHAT'S NEXT HEADING [MEDIUM]:** Added "What's Next" heading (13pt bold #C00000) in `build_next_steps_page()` before the Member Authorization section.

- **Fix 9 — HEADER SEPARATOR [MEDIUM]:** `build_standard_header()` bottom border changed from `NAVY_HEX, 16` (2pt navy) → `'AAAAAA', 4` (0.5pt light gray).

- **Fix 10 — TOC [MEDIUM]:** Removed TOC section from `main()` (blank due to no Heading styles on section paragraphs). `build_toc_page()` function retained with `TODO` comment for future re-enable. Section 3 (Cover Letter) now calls `build_standard_header()` directly since it's the first content section post-TOC removal.

### src/services/assembleTemplateData.js

- **EL FEE [BLOCKER]:** `elFeeNum = 20` → `elFeeNum = 120`. JSDoc comment updated. The $20 value was rendering as $0.00 due to floating point — confirmed the actual fix is setting the constant to 120 as per WI.

### src/services/documentRenderer.js

- **Dual-logo loading:** Already correct. `loadNamedLogos()` loads `stacked` and `horizontal` buffers separately. `assembleNbaisWcTemplateData` receives them as `logos.stacked` / `logos.horizontal` and returns `stackedLogoBase64` and `horizontalLogoBase64`. No changes needed.

## Visual Verification

Compared generated output against `review-assets/ado2594/` reference images (Jay's reference). Key visual issues from reference images addressed:
- Logo placeholder now uses correct `{%}` syntax (will render after Rhodey deploys JS fix)
- Page proportions corrected from A3 (12.2×15.8in) → Letter (8.5×11in)
- Cover title updated to full WC-specific title
- Banner spacing matches reference proportions
- Cell padding uniform across all tables

## S3 Sync

```
upload: templates/verticals/nbais-wc/master.docx to s3://fortress-tools/fip-proposal-templates/verticals/nbais-wc/master.docx
```
Synced to `s3://fortress-tools/fip-proposal-templates/verticals/nbais-wc/` via `--profile fortress-tools-deployer`.

## Service Test

```
curl POST /proposals/generate → OK (proposalId returned)
```
Live service (stale image) generates successfully with new template — no breaking merge field changes.

## Self-Review Checklist

- [x] All pages 8.5x11 — verified `<w:pgSz w:w="12240" w:h="15840"/>` on all 8 sections
- [x] Stacked logo tag present in cover section (`{%stackedLogoBase64}`) + assembler returns `stackedLogoBase64`
- [x] EL fee = 120 — `elFeeNum = 120` in assembleTemplateData.js
- [x] Banner heights padded (`space_before/after = Pt(8)`), full-width (CONTENT_W=9360)
- [x] Uniform cell margins (80/80/115/115 twips default)
- [x] KV column ratio ~35/65 (3276/6084 twips at CONTENT_W=9360)
- [x] Total row bold navy text — already correct, verified
- [x] Whats-next heading #C00000 — added before Member Authorization section
- [x] Header rule thin light gray — AAAAAA, 4 half-points (0.5pt)
- [x] TOC removed for v1 (function retained with TODO comment)

## Notes for Clint

- Logo rendering fix (item 2) requires Rhodey to deploy the JS changes (`assembleTemplateData.js`). Template tag is correct; the deployed image still has the old code.
- Cover letter section (s3) now calls `build_standard_header()` directly since TOC section was removed — header chain is intact.
- `apply_standard_margins()` now sets `page_width`/`page_height` in addition to margins, ensuring all `add_section()` calls produce correct letter-size pages regardless of Word defaults.
