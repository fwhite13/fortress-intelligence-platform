# REVIEW Assignment — ADO#2732 Cycle 2
## NBAIS WC template v2 — remove empty paragraphs from docx XML

**WI:** ADO#2732 (Legacy Work)
**Commit:** `4abb523`
**Build Report:** `services/proposal-generator/pipeline/ADO2732-BUILD-REPORT-C2.md`
**Review cycle:** 2 of 2

---

## Context

Cycle 1 review found that `a64c6ab` regressed two fixes from `ce8a2b5` and Fixes 2/3/5 were never applied. Tony has now:
1. Restored the `ce8a2b5` baseline (Fix 1 + Fix 4 correct)
2. Applied Fix 2, Fix 3, Fix 5 via Python/lxml
3. Run a verification script confirming 5/5 checks pass
4. Synced directly to S3 (did NOT use `--sync` flag — that rebuilds from source and would wipe XML edits)

Your job: independently verify all 5 fixes in the current `master.docx` XML.

---

## MANDATORY: Use Claude Code CLI

Write a review brief, then:
```bash
cd /home/fredw/projects/fip
cat services/proposal-generator/pipeline/ADO2732-C2-REVIEW-CC-BRIEF.md | \
  claude --model sonnet --print --dangerously-skip-permissions
```

Report MUST include the CC invocation used.

---

## How to Inspect

```bash
cd /home/fredw/projects/fip/services/proposal-generator/templates/verticals/nbais-wc/
unzip -p master.docx word/document.xml > /tmp/ado2732-c2-review-doc.xml
```

Then use the following Python/lxml verification script to check all 5 fixes:

```python
import zipfile, lxml.etree as ET

W = 'http://schemas.openxmlformats.org/wordprocessingml/2006/main'
ns = {'w': W}

def is_empty_para(p):
    return len(p.findall('w:r', ns)) == 0 and len(p.findall('w:hyperlink', ns)) == 0

def cell_para_count_for_tag(tables, tag):
    for i, tbl in enumerate(tables):
        tbl_text = ET.tostring(tbl, encoding='unicode')
        if tag not in tbl_text:
            continue
        for row in tbl.findall('w:tr', ns):
            for cell in row.findall('w:tc', ns):
                texts = ''.join(t.text or '' for t in cell.findall('.//w:t', ns))
                if tag in texts:
                    paras = cell.findall('w:p', ns)
                    return i, texts[:100], len(paras)
    return None, None, None

docx_path = 'services/proposal-generator/templates/verticals/nbais-wc/master.docx'
with zipfile.ZipFile(docx_path) as z:
    xml = z.read('word/document.xml')

root = ET.fromstring(xml)
tables = root.findall('.//w:tbl', ns)
print(f"Total tables: {len(tables)}")

# Fix 1: No leading empty paras in any cell
print("\n--- Fix 1: Leading empty paras in table cells ---")
found_any = False
for i, tbl in enumerate(tables):
    for j, row in enumerate(tbl.findall('w:tr', ns)):
        for k, cell in enumerate(row.findall('w:tc', ns)):
            paras = cell.findall('w:p', ns)
            if paras and is_empty_para(paras[0]):
                print(f"  FAIL: Table {i}, row {j}, cell {k}: leading empty para")
                found_any = True
if not found_any:
    print("  PASS: No leading empty paras found")

# Fix 2: {#classSchedule}+{state} single para
print("\n--- Fix 2: {{#classSchedule}}+{{state}} ---")
i, text, count = cell_para_count_for_tag(tables, '{#classSchedule}')
print(f"  Table {i}: '{text}' — {count} para(s) {'PASS' if count==1 else 'FAIL'}")

# Fix 3: {classEstPremium}+{/classSchedule} single para
print("\n--- Fix 3: {{classEstPremium}}+{{/classSchedule}} ---")
i, text, count = cell_para_count_for_tag(tables, '{classEstPremium}')
print(f"  Table {i}: '{text}' — {count} para(s) {'PASS' if count==1 else 'FAIL'}")

# Fix 4: {#excludedPersons}+{name} single para
print("\n--- Fix 4: {{#excludedPersons}}+{{name}} ---")
i, text, count = cell_para_count_for_tag(tables, '{#excludedPersons}')
print(f"  Table {i}: '{text}' — {count} para(s) {'PASS' if count==1 else 'FAIL'}")

# Fix 5: Form D-43+{/excludedPersons} single para
print("\n--- Fix 5: Form D-43+{{/excludedPersons}} ---")
i, text, count = cell_para_count_for_tag(tables, '{/excludedPersons}')
print(f"  Table {i}: '{text}' — {count} para(s) {'PASS' if count==1 else 'FAIL'}")
```

Run from `/home/fredw/projects/fip/`. All 5 must show PASS.

---

## Verdict Criteria

| Verdict | Condition |
|---------|-----------|
| **PASS** | All 5 checks PASS in verification script output |
| **NEEDS-CHANGES** | Any check FAIL |

This is cycle 2 of 2. If NEEDS-CHANGES, Maria escalates — do not hold back findings.

---

## ADO Comment

Post to ADO#2732 (project="Legacy Work"):

**If PASS:**
```
**[Hawkeye — REVIEW cycle 2]**
Verification script 5/5 PASS: Fix 1 (0 leading empty paras), Fix 2 ({#classSchedule}+{state} 1 para), Fix 3 ({classEstPremium}+{/classSchedule} 1 para), Fix 4 ({#excludedPersons}+{name} 1 para), Fix 5 (Form D-43+{/excludedPersons} 1 para). Verdict: PASS.
```

**If NEEDS-CHANGES:**
```
**[Hawkeye — REVIEW cycle 2]**
Verdict: NEEDS-CHANGES. [specific failures from verification script]
```

```bash
mcporter call devops.add_comment project="Legacy Work" id=2732 text="**[Hawkeye — REVIEW cycle 2]**\n..."
```

---

## Deliverable

Write review report to `services/proposal-generator/pipeline/ADO2732-REVIEW-REPORT-C2.md` with:
1. CC invocation used
2. Verification script output (verbatim)
3. Verdict: PASS / NEEDS-CHANGES
4. Each fix: ✅ or ❌ with evidence
