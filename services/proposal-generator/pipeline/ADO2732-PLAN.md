# BUILD Assignment — ADO#2732
## Proposal Generator: NBAIS WC template v2 — remove empty paragraphs from docx XML (page 5 + pages 7-9)

**WI:** ADO#2732 (Legacy Work)
**Risk:** low
**Repo:** `/home/fredw/projects/fip/`
**Service:** `services/proposal-generator/`
**Template:** `services/proposal-generator/templates/verticals/nbais-wc/master.docx`

---

## Root Cause

All remaining layout issues are caused by empty `<w:p/>` paragraphs baked into `master.docx` `word/document.xml`. JS trim fixes cannot help — the problem is in the template XML itself. All 5 fixes are direct XML edits to `word/document.xml`.

---

## MANDATORY: Use Claude Code CLI

Write a brief file at `services/proposal-generator/pipeline/ADO2732-CC-BRIEF.md`, then:

```bash
cd /home/fredw/projects/fip
cat services/proposal-generator/pipeline/ADO2732-CC-BRIEF.md | \
  CLAUDE_CODE_ENTRYPOINT=ado-pipeline \
  CLAUDE_CODE_DISABLE_AUTO_MEMORY=1 \
  CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1 \
  CLAUDE_CODE_GLOB_TIMEOUT_SECONDS=30 \
  claude --model sonnet --print --dangerously-skip-permissions
```

---

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

**Important:** After editing, the file `master.docx` itself is updated in-place. `word/document.xml` inside it now contains your edits.

---

## Fix 1 — Pages 7-9: Remove Leading Empty Paragraph from Every Two-Column Table Cell

**What to find:** Every `<w:tc>` in every two-column boilerplate table on pages 7-9 starts with an empty paragraph before the first content paragraph:
```xml
<w:tc>
  <w:tcPr>...</w:tcPr>
  <w:p><w:pPr>...</w:pPr></w:p>   ← REMOVE THIS
  <w:p>... actual content ...</w:p>
</w:tc>
```

**What to do:** Remove the leading empty `<w:p>` (the one with only `<w:pPr>` and no `<w:r>` runs) from EVERY cell in EVERY two-column table on pages 7-9.

**How to identify the pages 7-9 tables:** These are the boilerplate recommendation tables — they contain docxtemplater loop tags like `{#commercialLinesItems}`, `{#personalLinesItems}`, `{#bondItems}` etc. (Commercial Lines, Personal Lines, Bond Recommendations sections). They are two-column tables. Look for `<w:tbl>` elements containing these loop tags.

**Why this matters:**
- Fixes the blank line between section headings ("Commercial Lines", "Personal Lines", "Bond Recommendations") and the first sub-heading
- Fixes "Farm & Ranch" appearing lower than "Automobile" in the Personal Insurance section — both cells had empty para 0, content para 1; removing it makes both cells start at content immediately so right-column items align with left-column items

**Approach:** Use Python (lxml or string manipulation) to parse the XML, find all `<w:tbl>` blocks that contain the loop tags, then strip leading empty `<w:p>` elements from all `<w:tc>` children. An empty `<w:p>` is defined as a `<w:p>` that has no `<w:r>` or `<w:hyperlink>` children (may have `<w:pPr>` only).

---

## Fix 2 — Page 5, Classification Schedule: Remove Trailing Empty Paragraph from `{classEstPremium}` Cell

**What to find:** In the classification schedule template row, the `{classEstPremium}` cell:
```xml
<w:tc>
  ...
  <w:p>...<w:t>{classEstPremium}</w:t>...</w:p>
  <w:p><w:pPr>...</w:pPr></w:p>   ← REMOVE THIS
</w:tc>
```

**What to do:** Find the `<w:tc>` containing `{classEstPremium}` and remove any trailing empty `<w:p>` (no `<w:r>` children) after the tag paragraph.

---

## Fix 3 — Page 5, Classification Schedule: Remove Leading Space from `{state}` Cell

**What to find:** The `<w:t>` element containing `{state}` may have a leading space:
```xml
<w:t xml:space="preserve"> {state}</w:t>
```
or just:
```xml
<w:t> {state}</w:t>
```

**What to do:** Find `<w:t>` containing `{state}`. If the text starts with one or more spaces before `{state}`, remove them. The result should be exactly `<w:t>{state}</w:t>` (no leading space, no `xml:space` attribute needed unless there's a trailing space).

---

## Fix 4 — Page 5, Excluded Persons: Consolidate Loop Tag and Name Tag onto Single Paragraph

**What to find:** The Name cell in the excluded persons loop row has two paragraphs:
```xml
<w:tc>
  ...
  <w:p>...<w:t>{#excludedPersons}</w:t>...</w:p>   ← loop open tag para
  <w:p>...<w:t>{name}</w:t>...</w:p>               ← name para
</w:tc>
```

**What to do:** Consolidate these onto a single `<w:p>`. The loop open tag `{#excludedPersons}` and the name `{name}` should be in the same paragraph. The simplest approach: move the `<w:r>` run containing `{#excludedPersons}` into the same `<w:p>` as `{name}`, then delete the now-empty first paragraph.

Result:
```xml
<w:tc>
  ...
  <w:p>...<w:r><w:t>{#excludedPersons}</w:t></w:r><w:r><w:t>{name}</w:t></w:r>...</w:p>
</w:tc>
```

**Why:** Two paragraphs in a cell = visible blank line before the name content.

---

## Fix 5 — Page 5, Excluded Persons: Remove Trailing Empty Paragraph from `{electionForm}` Cell

**What to find:** The "Form D-43 — Election to Reject Coverage" cell (which contains `{electionForm}` tag) has a trailing empty `<w:p>`:
```xml
<w:tc>
  ...
  <w:p>...<w:t>{electionForm}</w:t>...</w:p>
  <w:p><w:pPr>...</w:pPr></w:p>   ← REMOVE THIS
</w:tc>
```

**What to do:** Same as Fix 2. Find the `<w:tc>` containing `{electionForm}` and remove any trailing empty `<w:p>` after the tag paragraph.

---

## After Applying XML Fixes

Run full generation and verify:

```bash
cd /home/fredw/projects/fip
node services/proposal-generator/scripts/generate-proposal.js \
  services/proposal-generator/test-payloads/nbais-wc-test.json \
  /tmp/ado2732-test.docx 2>&1 | head -30
```

If generation succeeds (no errors, file created), then sync to S3 and rebuild template:

```bash
cd /home/fredw/projects/fip/services/proposal-generator
python3 scripts/build-nbais-wc-template.py --sync
```

**Expected:** No Python/Node errors, template synced to S3.

---

## Commit

```
fix(ADO#2732): remove empty w:p from master.docx XML (page 5 + pages 7-9)

Fix 1: Strip leading empty <w:p> from all cells in two-column boilerplate tables (pages 7-9)
Fix 2: Remove trailing empty <w:p> from classEstPremium cell (classification schedule)
Fix 3: Remove leading space from {state} text run (classification schedule)
Fix 4: Consolidate {#excludedPersons} + {name} onto single paragraph (excluded persons name cell)
Fix 5: Remove trailing empty <w:p> from electionForm cell (excluded persons)
```

---

## ADO Comment

After commit, post to ADO#2732:

```
**[Tony Stark — BUILD cycle 1]**
Commit {hash}: 5 XML fixes applied to master.docx word/document.xml. Fix 1 (leading empty para all 2-col table cells pages 7-9), Fix 2 (trailing empty para classEstPremium), Fix 3 (leading space {state}), Fix 4 (consolidate excludedPersons+name para), Fix 5 (trailing empty para electionForm). Build: SUCCEEDED. Template synced to S3.
```

```bash
mcporter call devops.add_comment project="Legacy Work" id=2732 text="**[Tony Stark — BUILD cycle 1]**\nCommit {hash}: 5 XML fixes applied to master.docx word/document.xml. Fix 1 (leading empty para all 2-col table cells pages 7-9), Fix 2 (trailing empty para classEstPremium), Fix 3 (leading space {state}), Fix 4 (consolidate excludedPersons+name para), Fix 5 (trailing empty para electionForm). Build: SUCCEEDED. Template synced to S3."
```

---

## Build Report

Write to `services/proposal-generator/pipeline/ADO2732-BUILD-REPORT.md`:

```markdown
# Build Report — ADO#2732
**Status:** SUCCEEDED / FAILED
**CC invocation:** [exact command used]
**Commit:** {hash}
**Files changed:** master.docx (word/document.xml patched in-place)

## Fixes Applied
- Fix 1 (Pages 7-9 leading empty para): {cells patched count}
- Fix 2 (classEstPremium trailing para): {done/not found}
- Fix 3 ({state} leading space): {done/not found}
- Fix 4 (excludedPersons consolidate): {done}
- Fix 5 (electionForm trailing para): {done/not found}

## Generation Test
{output of generate-proposal.js — success or errors}

## S3 Sync
{output of build-nbais-wc-template.py --sync — success or errors}
```

---

*Five XML fixes, all in `word/document.xml`. No JS changes. No Python builder changes. Just the template XML.*
