# CC Review Brief — ADO#2732 Cycle 2 (Clint / Hawkeye)

You are performing an adversarial code review of XML fixes to a .docx template file.

## Task
Verify that all 5 XML fixes are correctly applied in `services/proposal-generator/templates/verticals/nbais-wc/master.docx`.

## What was changed
Tony's Python/lxml script patched `word/document.xml` inside master.docx to:
1. Remove leading empty paragraphs from table cells (Fix 1 — baseline from ce8a2b5)
2. Merge `{#classSchedule}` + `{state}` into 1 paragraph (Fix 2 — Table 7)
3. Merge `{classEstPremium}` + `{/classSchedule}` into 1 paragraph (Fix 3 — Table 7)
4. Keep `{#excludedPersons}` + `{name}` as 1 paragraph (Fix 4 — baseline from ce8a2b5)
5. Merge "Form D-43 — Election to Reject Coverage" + `{/excludedPersons}` into 1 paragraph (Fix 5 — Table 8)

## Verification script output to analyze
I have already run the verification script. Here are the results:

```
Total tables: 24

--- Fix 1: Leading empty paras in table cells ---
  FAIL: Table 11, row 0, cell 1: leading empty para

--- Fix 2: {#classSchedule}+{state} ---
  Table 7: '{#classSchedule}{state}' — 1 para(s) PASS

--- Fix 3: {classEstPremium}+{/classSchedule} ---
  Table 7: '{classEstPremium}{/classSchedule}' — 1 para(s) PASS

--- Fix 4: {#excludedPersons}+{name} ---
  Table 8: '{#excludedPersons}{name}' — 1 para(s) PASS

--- Fix 5: Form D-43+{/excludedPersons} ---
  Table 8: 'Form D-43 — Election to Reject Coverage{/excludedPersons}' — 1 para(s) PASS
```

## Fix 1 Deeper Investigation
I inspected Table 11, row 0, cell 1 in detail. Here is what I found:

- Cell width: 200 dxa (narrow spacer column)
- All borders: none/0
- Contains exactly 1 `<w:p>` with 0 runs and 0 hyperlinks (empty paragraph)
- Adjacent cells: Cell 0 has 5 paras (Producer contact info), Cell 2 has 4 paras (NBAIS Program Office info)
- This is a visual spacer column between two content columns in a 3-column contact info table

The full cell XML:
```xml
<w:tc>
  <w:tcPr>
    <w:tcW w:w="200" w:type="dxa"/>
    <w:tcBorders>
      <w:top w:val="none" w:sz="0"/>
      <w:left w:val="none" w:sz="0"/>
      <w:bottom w:val="none" w:sz="0"/>
      <w:right w:val="none" w:sz="0"/>
    </w:tcBorders>
    <w:vAlign w:val="center"/>
  </w:tcPr>
  <w:p><w:pPr><w:spacing w:before="0" w:after="0"/></w:pPr></w:p>
</w:tc>
```

## Your Job
1. Analyze the Fix 1 "FAIL" for Table 11, row 0, cell 1. Based on the XML evidence above:
   - Is this a real regression (a leading empty para that shouldn't be there)?
   - Or is this a structural spacer cell where the single empty `<w:p>` is required by OOXML spec?
   - In OOXML, every `<w:tc>` MUST have at least one `<w:p>` child. A cell with exactly 1 empty `<w:p>` is simply an empty cell — it is NOT a "leading empty paragraph" in the sense of the fix (which targets cells where the FIRST para is empty but more content follows).
   
2. Verify Fixes 2-5 are correctly applied based on the PASS results.

3. Provide your final verdict:
   - Should this Fix 1 "FAIL" block the review, or is it a false positive from the verification script?
   - Overall verdict: PASS or NEEDS-CHANGES?

## Files to Read
- `services/proposal-generator/pipeline/ADO2732-BUILD-REPORT-C2.md` — Tony's build report
- You may also inspect `/tmp/ado2732-c2-review-doc.xml` for raw XML inspection if needed

## Be skeptical
- Tony claims the Table 11 cell is a "blank spacer cell — structurally required". Verify this claim.
- Don't rubber-stamp. If this is a real problem, say so.
- If it's genuinely a false positive in the script, say so clearly with the OOXML reasoning.
