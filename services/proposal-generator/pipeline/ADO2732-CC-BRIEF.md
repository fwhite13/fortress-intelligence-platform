# CC Brief — ADO#2732: NBAIS WC master.docx XML Fixes

## Context
Apply 5 XML fixes to `services/proposal-generator/templates/verticals/nbais-wc/master.docx` by extracting `word/document.xml`, editing it, and repacking.

## Working Directory
`/home/fredw/projects/fip`

## How to Edit the .docx
The .docx is a zip file. Extract, edit, repack:
```bash
cd /home/fredw/projects/fip/services/proposal-generator/templates/verticals/nbais-wc/
unzip master.docx word/document.xml -d master_tmp
# edit master_tmp/word/document.xml
cd master_tmp && zip -u ../master.docx word/document.xml
cd ..
rm -rf master_tmp
```

## Implementation Plan
Write a Python script at `/tmp/ado2732-fix.py` that:
1. Extracts `word/document.xml` from the docx
2. Applies all 5 fixes using lxml
3. Repacks the docx

Then run: `python3 /tmp/ado2732-fix.py`

The script should print a summary of what it changed (e.g., "Fix 1: patched N cells", "Fix 2: done", etc.).

## Fix 1 — Pages 7-9: Remove Leading Empty Paragraph from Every Two-Column Table Cell

Find all `<w:tbl>` elements that contain loop tags like `{#commercialLinesItems}`, `{#personalLinesItems}`, `{#bondItems}` (or similar docxtemplater loop tags in the boilerplate recommendation sections). These are the two-column recommendation tables on pages 7-9.

For every `<w:tc>` in these tables, if the FIRST child `<w:p>` has NO `<w:r>` or `<w:hyperlink>` children (only `<w:pPr>` or nothing), remove that leading empty `<w:p>`.

An empty `<w:p>` = a `<w:p>` element that has no `<w:r>` children and no `<w:hyperlink>` children (it may have `<w:pPr>`).

## Fix 2 — Page 5, Classification Schedule: Remove Trailing Empty Paragraph from `{classEstPremium}` Cell

Find the `<w:tc>` containing a `<w:t>` with text containing `{classEstPremium}`. Remove any trailing `<w:p>` elements (after the content paragraph) that have no `<w:r>` children.

## Fix 3 — Page 5, Classification Schedule: Remove Leading Space from `{state}` Cell

Find the `<w:t>` element whose text content contains `{state}`. If the text starts with one or more spaces before `{state}`, strip those leading spaces. Remove the `xml:space="preserve"` attribute if the only reason it existed was the leading space (i.e., if after trimming there's no leading/trailing space remaining).

Result should be `<w:t>{state}</w:t>` (no leading space, xml:space attribute removed if no longer needed).

## Fix 4 — Page 5, Excluded Persons: Consolidate Loop Tag and Name Tag onto Single Paragraph

Find the `<w:tc>` that contains BOTH:
- A `<w:p>` with `{#excludedPersons}` in a `<w:t>`
- A `<w:p>` with `{name}` in a `<w:t>`

Consolidate: move the `<w:r>` run containing `{#excludedPersons}` into the same `<w:p>` as the one containing `{name}`. The `{#excludedPersons}` run should come BEFORE the `{name}` run. Delete the now-empty first paragraph.

Result: single `<w:p>` containing `{#excludedPersons}` run followed by `{name}` run.

## Fix 5 — Page 5, Excluded Persons: Remove Trailing Empty Paragraph from `{electionForm}` Cell

Find the `<w:tc>` containing a `<w:t>` with text containing `{electionForm}`. Remove any trailing `<w:p>` elements (after the content paragraph) that have no `<w:r>` children.

## Expected Script Output
The script must print fix-by-fix results, e.g.:
```
Fix 1: Patched N cells across M tables
Fix 2: Done (removed trailing empty para from classEstPremium cell)
Fix 3: Done (removed leading space from {state} text run)
Fix 4: Done (consolidated {#excludedPersons} + {name} onto single para)
Fix 5: Done (removed trailing empty para from electionForm cell)
All fixes applied. master.docx updated.
```

If any fix item is NOT found, print "NOT FOUND" for that fix (do not error out — continue with remaining fixes).

## IMPORTANT: Namespace Handling
The XML uses Word namespaces. Use lxml with namespace-aware parsing. The Word namespace is `http://schemas.openxmlformats.org/wordprocessingml/2006/main` (prefix `w:`).

When searching for text in `<w:t>` elements, search the text content, not attributes.

## After the Script
Do NOT run the generation test or S3 sync — those will be run separately. Just apply the fixes, repack the docx, and report results.
