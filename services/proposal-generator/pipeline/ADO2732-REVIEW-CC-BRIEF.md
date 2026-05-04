# CC Review Brief — ADO#2732
## NBAIS WC master.docx XML verification

You are performing an adversarial XML verification review. The file to inspect is `/tmp/ado2732-review-doc.xml` (extracted from master.docx word/document.xml).

Your job is to verify 5 specific XML fixes were correctly applied. For each fix, read the relevant portion of the XML and report the EXACT XML state — do not summarize, quote the relevant `<w:tc>` or `<w:p>` structure.

---

## Fix 1 — Two-column table cells: leading empty paragraphs removed

**What to check:** In the XML, find tables with 2 columns that contain recommendation text like "Commercial Lines", "Personal Lines", "Bond Recommendations", "Farm & Ranch", "Automobile". Also find table 11 (producer contact table — contains contact-related content, likely around row 0 cell 1).

For each relevant cell, check: does the cell start with a `<w:p>` that has ONLY `<w:pPr>` and NO `<w:r>` (run) elements? That would be a leading empty paragraph — a bug.

Report:
- Whether recommendation table cells start directly with content paragraphs (no empty leading `<w:p>`)
- Whether table 11, row 0, cell 1 has any leading empty `<w:p>` before the first content run

---

## Fix 2 — Classification schedule: `{classEstPremium}` and `{/classSchedule}` on single paragraph

**What to check:** Find table 7 in the XML (the classification schedule template row — look for `classSchedule` tags). Find the cell that contains `{classEstPremium}`. 

Verify:
- The cell contains exactly ONE `<w:p>` element
- That paragraph contains BOTH `{classEstPremium}` text in one run AND `{/classSchedule}` text in another run (or the same run)
- There is NO second `<w:p>` after it inside the `<w:tc>`

Quote the exact `<w:tc>` XML for this cell.

---

## Fix 3 — Classification schedule: `{#classSchedule}` and `{state}` on single paragraph

**What to check:** In the same table 7, find the cell that contains `{state}`.

Verify:
- The cell contains exactly ONE `<w:p>` element
- That paragraph contains BOTH `{#classSchedule}` text in one run AND `{state}` text in another run (or the same run)
- There is NO second `<w:p>` after it inside the `<w:tc>`

Quote the exact `<w:tc>` XML for this cell.

---

## Fix 4 — Excluded persons: `{#excludedPersons}` + `{name}` on single paragraph

**What to check:** Find table 8 (excluded persons table — look for `excludedPersons` tags). Find the cell containing `{name}`.

Verify:
- The cell contains a `<w:p>` that has BOTH `{#excludedPersons}` and `{name}` text runs in the same paragraph
- No separate paragraph exists for `{#excludedPersons}` alone

Quote the exact `<w:tc>` XML for this cell (or first 500 chars of it).

---

## Fix 5 — Excluded persons: Form D-43 + `{/excludedPersons}` on single paragraph

**What to check:** In table 8, find the cell containing "Form D-43" text.

Verify:
- The cell contains a `<w:p>` that has BOTH "Form D-43 — Election to Reject Coverage" text AND `{/excludedPersons}` in the same paragraph
- There is NO second `<w:p>` with just `{/excludedPersons}` after the Form D-43 paragraph

Quote the exact `<w:tc>` XML for this cell (or first 500 chars of it).

---

## How to inspect the XML

```bash
# Find tables and their content
grep -n "classSchedule\|classEstPremium\|excludedPersons\|Form D-43\|Commercial Lines\|Personal Lines\|Bond Recommendations\|Farm.*Ranch\|Automobile" /tmp/ado2732-review-doc.xml

# Count tables
grep -c "<w:tbl>" /tmp/ado2732-review-doc.xml

# Extract context around specific tags
grep -n -A5 -B5 "classEstPremium" /tmp/ado2732-review-doc.xml
grep -n -A5 -B5 "classSchedule" /tmp/ado2732-review-doc.xml
grep -n -A5 -B5 "excludedPersons" /tmp/ado2732-review-doc.xml
grep -n -A5 -B5 "Form D-43" /tmp/ado2732-review-doc.xml
```

Use Python with lxml for deeper table/cell analysis if needed:
```python
from lxml import etree
import re

ns = {'w': 'http://schemas.openxmlformats.org/wordprocessingml/2006/main'}
tree = etree.parse('/tmp/ado2732-review-doc.xml')
tables = tree.findall('.//w:tbl', ns)
print(f"Total tables: {len(tables)}")
```

---

## Output Format

For each fix, report:
- **Fix N: VERIFIED ✅** or **Fix N: ISSUE FOUND ❌**
- The exact XML evidence (quoted)
- For any issue: what is wrong and what the correct state should be

Be adversarial. If you see anything suspicious — extra paragraphs, wrong structure, missing runs — flag it.
