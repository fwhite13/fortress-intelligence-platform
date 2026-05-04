# Build Report — ADO#2732
**Status:** SUCCEEDED
**CC invocation:** Claude Code CLI (via `/tmp/ado2732-fix-v2.py` Python script — CC timed out, fixes applied directly via Python/lxml)
**Commit:** a64c6ab
**Files changed:** `services/proposal-generator/templates/verticals/nbais-wc/master.docx` (word/document.xml patched in-place)

---

## Fixes Applied

- **Fix 1 (Pages 7-9 leading empty para):** 1 cell patched — found leading empty `<w:p>` in table 11, row 0, cell 1 (producer contact table, blank middle cell). Note: The plan referenced loop tags (`{#commercialLinesItems}` etc.) that don't exist in this template; applied fix to all 2-col non-template tables instead. Recommendation tables 14-22 were already clean.

- **Fix 2 (classEstPremium trailing para):** Done — actual state was `{classEstPremium}` and `{/classSchedule}` on separate paragraphs (not a trailing empty para as described). Consolidated `{/classSchedule}` run into the `{classEstPremium}` paragraph, removing the blank line between class schedule rows.

- **Fix 3 ({state} leading space):** Done — actual state was `{#classSchedule}` on para 0 and `{state}` on para 1 in the same cell (no leading space found as described). Consolidated both tags onto a single paragraph, removing the blank line. `{state}` text had no leading space — already clean.

- **Fix 4 (excludedPersons consolidate):** Already done — `{#excludedPersons}` and `{name}` were already in the same `<w:p>`. No change needed.

- **Fix 5 (electionForm trailing para):** Done — tag `{electionForm}` not found; actual structure was `Form D-43 — Election to Reject Coverage` on para 0 and `{/excludedPersons}` on para 1 in the election form cell. Consolidated `{/excludedPersons}` run into the Form D-43 paragraph, removing the blank line after election form content.

---

## Generation Test

```
Loading master.docx...
Creating Docxtemplater instance...
Rendering template...
SUCCESS — output written to /tmp/ado2732-test-output.docx (423314 bytes)
```

Docxtemplater rendered template successfully with test data (classSchedule loop, excludedPersons loop, all template tags resolved). No errors.

---

## S3 Sync

```
Completed 413.2 KiB/413.2 KiB (14.8 MiB/s) with 1 file(s) remaining
upload: .../master.docx to s3://fortress-tools/fip-proposal-templates/verticals/nbais-wc/master.docx
```

S3 sync succeeded. Template uploaded to `s3://fortress-tools/fip-proposal-templates/verticals/nbais-wc/master.docx`.

---

## Notes

The build plan described the **problem state** as it existed when the plan was written. The actual template XML state at build time had some differences:

1. The plan's Fix 1 referenced loop tags (`{#commercialLinesItems}`, etc.) that aren't in this template — the recommendation tables use static text, not docxtemplater loops. Fix was applied to all applicable 2-col tables.
2. Fix 2 and Fix 3 had the correct cells but slightly different XML structure (loop close tags on separate lines vs. empty paras).
3. Fix 4 was already applied in a prior edit.
4. Fix 5: `{electionForm}` tag doesn't exist — `{/excludedPersons}` was the stray second paragraph.

All fixes result in the same outcome described in the plan: no blank lines caused by stray `<w:p>` elements in table cells.

---

## Verified XML State After Fixes

**Table 7 (class schedule template row):**
- Cell 0: `{#classSchedule}{state}` — single paragraph ✅
- Cell 5: `{classEstPremium}{/classSchedule}` — single paragraph ✅

**Table 8 (excluded persons):**
- Cell 0: `{#excludedPersons}{name}` — single paragraph ✅
- Cell 1: `Form D-43 — Election to Reject Coverage{/excludedPersons}` — single paragraph ✅
