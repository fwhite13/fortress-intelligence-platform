# BUILD Assignment — ADO#2732 Fix Cycle 2

**WI:** ADO#2732 (Legacy Work)
**Repo:** `/home/fredw/projects/fip/`
**Prior commits:** `ce8a2b5` (partial — Fix 1 + Fix 4 correct), `a64c6ab` (HEAD — regressed Fix 1 + Fix 4, Fixes 2/3/5 not applied)
**Review cycle:** 2 of 2

---

## What Went Wrong

Your HEAD commit `a64c6ab` regressed two fixes that `ce8a2b5` had correctly applied (Fix 1 and Fix 4), and Fixes 2, 3, and 5 were never applied. The generation test passing does not validate XML structure — Docxtemplater renders blank paragraphs as blank lines without errors.

**You must:**
1. Revert `a64c6ab` (which regressed the prior correct state)
2. Apply all 5 fixes correctly from the `ce8a2b5` baseline

---

## MANDATORY: Use Claude Code CLI

Write a brief at `services/proposal-generator/pipeline/ADO2732-C2-CC-BRIEF.md`, then:

```bash
cd /home/fredw/projects/fip
cat services/proposal-generator/pipeline/ADO2732-C2-CC-BRIEF.md | \
  CLAUDE_CODE_ENTRYPOINT=ado-pipeline \
  CLAUDE_CODE_DISABLE_AUTO_MEMORY=1 \
  CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1 \
  CLAUDE_CODE_GLOB_TIMEOUT_SECONDS=30 \
  claude --model sonnet --print --dangerously-skip-permissions
```

---

## Step 0 — Revert HEAD and Reset to `ce8a2b5`

```bash
cd /home/fredw/projects/fip
git revert --no-commit a64c6ab
# OR: reset the file to ce8a2b5 state and work from there
git checkout ce8a2b5 -- services/proposal-generator/templates/verticals/nbais-wc/master.docx
```

The `ce8a2b5` state of `master.docx` has Fix 1 and Fix 4 already correct. Confirm this before proceeding.

---

## Working Method

The .docx is a zip. Edit `word/document.xml` using Python/lxml (write a script). Do NOT use string replacement — use lxml to parse and manipulate XML nodes precisely.

```bash
cd /home/fredw/projects/fip/services/proposal-generator/templates/verticals/nbais-wc/
unzip master.docx word/document.xml -d master_tmp
# run your Python/lxml fix script against master_tmp/word/document.xml
cd master_tmp && zip -u ../master.docx word/document.xml
cd .. && rm -rf master_tmp
```

---

## Verification Step — MANDATORY before Fixes 2/3/5

After restoring `ce8a2b5`, run this Python check to confirm Fix 1 and Fix 4 are in the correct state:

```python
import zipfile, lxml.etree as ET

W = 'http://schemas.openxmlformats.org/wordprocessingml/2006/main'

def is_empty_para(p):
    """True if <w:p> has no <w:r> or <w:hyperlink> children"""
    ns = {'w': W}
    return len(p.findall('w:r', ns)) == 0 and len(p.findall('w:hyperlink', ns)) == 0

with zipfile.ZipFile('master.docx') as z:
    xml = z.read('word/document.xml')

root = ET.fromstring(xml)
ns = {'w': W}
tables = root.findall('.//w:tbl', ns)

print(f"Total tables: {len(tables)}")
for i, tbl in enumerate(tables):
    for j, row in enumerate(tbl.findall('w:tr', ns)):
        for k, cell in enumerate(row.findall('w:tc', ns)):
            paras = cell.findall('w:p', ns)
            if paras and is_empty_para(paras[0]):
                print(f"  Table {i}, row {j}, cell {k}: leading empty para")

# Check Fix 4
for i, tbl in enumerate(tables):
    tbl_text = ET.tostring(tbl, encoding='unicode')
    if '{#excludedPersons}' in tbl_text:
        print(f"\nExcluded persons table: table {i}")
        for row in tbl.findall('w:tr', ns):
            for cell in row.findall('w:tc', ns):
                texts = [t.text or '' for t in cell.findall('.//w:t', ns)]
                combined = ''.join(texts)
                if '{#excludedPersons}' in combined or '{name}' in combined:
                    paras = cell.findall('w:p', ns)
                    print(f"  Cell text: {combined[:80]}, paragraphs: {len(paras)}")
```

Expected after restoring `ce8a2b5`:
- No leading empty paras in recommendation tables (14, 16, 17, 18, 20, 21, 22)
- `{#excludedPersons}{name}` cell: 1 paragraph

If the check shows regressions after restore, stop and diagnose before proceeding.

---

## Fix 2 — Table 7: Consolidate `{#classSchedule}` + `{state}` onto single paragraph

**Current state (both commits):**
```xml
<w:tc>  <!-- state cell in classification schedule -->
  ...
  <w:p>...<w:r><w:t>{#classSchedule}</w:t></w:r></w:p>   ← para 0
  <w:p>...<w:r><w:t>{state}</w:t></w:r></w:p>             ← para 1
</w:tc>
```

**Target state:**
```xml
<w:tc>
  ...
  <w:p>...<w:r><w:t>{#classSchedule}</w:t></w:r><w:r><w:t>{state}</w:t></w:r></w:p>
</w:tc>
```

**How (lxml):** Find Table 7, find the cell containing `{#classSchedule}`. Move all `<w:r>` runs from para 1 into para 0. Remove para 1.

---

## Fix 3 — Table 7: Consolidate `{classEstPremium}` + `{/classSchedule}` onto single paragraph

**Current state:**
```xml
<w:tc>  <!-- last cell in classification schedule row -->
  ...
  <w:p>...<w:r><w:t>{classEstPremium}</w:t></w:r></w:p>   ← para 0
  <w:p>...<w:r><w:t>{/classSchedule}</w:t></w:r></w:p>    ← para 1
</w:tc>
```

**Target state:**
```xml
<w:tc>
  ...
  <w:p>...<w:r><w:t>{classEstPremium}</w:t></w:r><w:r><w:t>{/classSchedule}</w:t></w:r></w:p>
</w:tc>
```

**How (lxml):** Find Table 7, find the cell containing `{classEstPremium}`. Move all `<w:r>` runs from para 1 into para 0. Remove para 1.

---

## Fix 5 — Table 8: Consolidate Form D-43 text + `{/excludedPersons}` onto single paragraph

**Current state:**
```xml
<w:tc>  <!-- election form cell in excluded persons row -->
  ...
  <w:p>...<w:r><w:t>Form D-43 — Election to Reject Coverage</w:t></w:r></w:p>   ← para 0
  <w:p>...<w:r><w:t>{/excludedPersons}</w:t></w:r></w:p>                         ← para 1
</w:tc>
```

**Target state:**
```xml
<w:tc>
  ...
  <w:p>...<w:r><w:t>Form D-43 — Election to Reject Coverage</w:t></w:r><w:r><w:t>{/excludedPersons}</w:t></w:r></w:p>
</w:tc>
```

**How (lxml):** Find Table 8, find the cell containing `Form D-43`. Move all `<w:r>` runs from para 1 into para 0. Remove para 1.

---

## Post-Fix Verification — MANDATORY

Run the same Python check again after applying fixes to confirm:
1. No leading empty paras in recommendation tables (Fix 1 still intact)
2. `{#excludedPersons}{name}` — 1 paragraph (Fix 4 still intact)
3. `{#classSchedule}{state}` cell — 1 paragraph (Fix 2 applied)
4. `{classEstPremium}{/classSchedule}` cell — 1 paragraph (Fix 3 applied)
5. `Form D-43...{/excludedPersons}` cell — 1 paragraph (Fix 5 applied)

If any check fails, fix it before committing.

---

## Generation Test

```bash
cd /home/fredw/projects/fip
node services/proposal-generator/scripts/generate-proposal.js \
  services/proposal-generator/test-payloads/nbais-wc-test.json \
  /tmp/ado2732-c2-test.docx 2>&1 | head -30
```

Expected: no errors, file created.

Then sync to S3:
```bash
cd /home/fredw/projects/fip/services/proposal-generator
python3 scripts/build-nbais-wc-template.py --sync
```

---

## Commit

Single commit with a clear message:
```
fix(ADO#2732): correctly apply all 5 XML fixes to master.docx word/document.xml

Revert regression from a64c6ab. Starting from ce8a2b5 baseline (Fix 1 + Fix 4 correct).
Fix 2: {#classSchedule}+{state} onto single para (Table 7 state cell)
Fix 3: {classEstPremium}+{/classSchedule} onto single para (Table 7 estPremium cell)
Fix 5: Form D-43+{/excludedPersons} onto single para (Table 8 electionForm cell)
Fix 1 (22 rec table cells) and Fix 4 (excludedPersons+name) restored from ce8a2b5.
```

---

## ADO Comment

Post to ADO#2732:

```
**[Tony Stark — BUILD cycle 2]**
Commit {hash}: Reverted regression from a64c6ab. All 5 XML fixes now correctly applied from ce8a2b5 baseline. Fix 1 (22 rec table cells clean), Fix 2 ({#classSchedule}+{state} 1 para), Fix 3 ({classEstPremium}+{/classSchedule} 1 para), Fix 4 ({#excludedPersons}+{name} 1 para, restored), Fix 5 (Form D-43+{/excludedPersons} 1 para). Verification script: 0 issues. Generation: clean. S3: synced.
```

```bash
mcporter call devops.add_comment project="Legacy Work" id=2732 text="**[Tony Stark — BUILD cycle 2]**\nCommit {hash}: Reverted regression from a64c6ab. All 5 XML fixes correctly applied from ce8a2b5 baseline. Fix 1 (22 rec table cells clean), Fix 2 ({#classSchedule}+{state} 1 para), Fix 3 ({classEstPremium}+{/classSchedule} 1 para), Fix 4 ({#excludedPersons}+{name} 1 para restored), Fix 5 (Form D-43+{/excludedPersons} 1 para). Verification: 0 issues. Generation: clean. S3: synced."
```

---

## Build Report

Write to `services/proposal-generator/pipeline/ADO2732-BUILD-REPORT-C2.md`:

```markdown
# Build Report — ADO#2732 Cycle 2
**Status:** SUCCEEDED / FAILED
**CC invocation:** [exact command]
**Commit:** {hash}
**Baseline:** ce8a2b5 (Fix 1 + Fix 4 correct)

## Verification script output (pre-fix)
{output confirming ce8a2b5 baseline state}

## Fixes applied
- Fix 2: {done}
- Fix 3: {done}
- Fix 5: {done}
- Fix 1 (retained from ce8a2b5): {confirmed}
- Fix 4 (retained from ce8a2b5): {confirmed}

## Verification script output (post-fix)
{output confirming all 5 fixes correct — 0 issues}

## Generation test
{output — success}

## S3 sync
{output — success}
```

---

*This is cycle 2 of 2. Get the XML right — run the verification script before committing.*
