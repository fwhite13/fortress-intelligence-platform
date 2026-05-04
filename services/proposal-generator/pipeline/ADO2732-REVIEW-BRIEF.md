# REVIEW Assignment — ADO#2732
## Proposal Generator: NBAIS WC template v2 — remove empty paragraphs from docx XML

**WI:** ADO#2732 (Legacy Work)
**Commit:** `a64c6ab`
**Build Report:** `services/proposal-generator/pipeline/ADO2732-BUILD-REPORT.md`
**Review cycle:** 1 of 2

---

## Context

All fixes are direct XML edits to `master.docx` `word/document.xml`. No JS or Python builder changes. The WI described 5 fixes; Tony adapted them to the actual XML state found (which had evolved since the WI was authored). Your job is to verify the XML state is now correct and the template renders cleanly.

---

## MANDATORY: Use Claude Code CLI

Write a review brief, then:
```bash
cd /home/fredw/projects/fip
cat services/proposal-generator/pipeline/ADO2732-REVIEW-CC-BRIEF.md | \
  claude --model sonnet --print --dangerously-skip-permissions
```

Your review report MUST include the CC invocation command used.

---

## How to Inspect the .docx XML

```bash
cd /home/fredw/projects/fip/services/proposal-generator/templates/verticals/nbais-wc/
unzip -p master.docx word/document.xml > /tmp/ado2732-review-doc.xml
# Now inspect /tmp/ado2732-review-doc.xml
```

---

## What to Verify

Read the Build Report first (`services/proposal-generator/pipeline/ADO2732-BUILD-REPORT.md`) to understand what Tony actually changed vs. what the plan described.

### Fix 1 — Pages 7-9 two-column table cells (leading empty para)
- In the build report Tony says recommendation tables 14-22 were already clean and only table 11 (producer contact) had 1 leading empty para.
- Verify: In the XML, do the two-column boilerplate/recommendation tables (containing text like "Commercial Lines", "Personal Lines", "Bond Recommendations", "Farm & Ranch", "Automobile") have cells that start directly with content paragraphs (no leading empty `<w:p>` with only `<w:pPr>` and no `<w:r>`)?
- Also check table 11 — confirm the empty leading para was removed from that cell.

### Fix 2 — Classification schedule: `{classEstPremium}` and `{/classSchedule}` on single para
- Tony says he consolidated `{/classSchedule}` into the same `<w:p>` as `{classEstPremium}`.
- Verify: In the classification schedule table (table 7), the last cell should contain a single paragraph with both `{classEstPremium}` and `{/classSchedule}` text runs. No second paragraph should follow inside the cell.

### Fix 3 — Classification schedule: `{#classSchedule}` and `{state}` on single para
- Tony says he consolidated `{#classSchedule}` from para 0 into the same `<w:p>` as `{state}`.
- Verify: In the classification schedule table (table 7), the first cell (state cell) should contain a single paragraph with both `{#classSchedule}` and `{state}` text runs. No second paragraph should follow inside the cell.

### Fix 4 — Excluded persons: `{#excludedPersons}` + `{name}` already on single para
- Tony says this was already consolidated before his work.
- Verify: In the excluded persons table (table 8), the name cell should contain a single paragraph with `{#excludedPersons}` and `{name}` text runs.

### Fix 5 — Excluded persons: Form D-43 + `{/excludedPersons}` on single para
- Tony says `{/excludedPersons}` was on a separate para from the Form D-43 text — he consolidated them.
- Verify: In the excluded persons table (table 8), the election form cell should contain a single paragraph with the "Form D-43 — Election to Reject Coverage" text and `{/excludedPersons}` run. No second paragraph inside the cell.

### Generation test
- Tony ran generation and got 423KB output with no errors. Confirm the build report shows a clean generation run.
- You do NOT need to re-run generation yourself unless you spot something suspicious in the XML.

---

## Verdict Criteria

| Verdict | Condition |
|---------|-----------|
| **PASS** | All 5 fixes verified in XML, generation was clean |
| **NEEDS-CHANGES** | Any fix is wrong/incomplete in the XML, or generation produced errors |

---

## ADO Comment

After verdict, post to ADO#2732:

**If PASS:**
```
**[Hawkeye — REVIEW cycle 1]**
XML verified in word/document.xml: Fix 1 (table cells clean), Fix 2 ({classEstPremium}/{/classSchedule} single para), Fix 3 ({#classSchedule}/{state} single para), Fix 4 ({#excludedPersons}/{name} single para), Fix 5 (Form D-43/{/excludedPersons} single para). Generation: 423KB clean. Verdict: PASS.
```

**If NEEDS-CHANGES:**
```
**[Hawkeye — REVIEW cycle 1]**
Verdict: NEEDS-CHANGES. [specific XML issues found]
```

```bash
mcporter call devops.add_comment project="Legacy Work" id=2732 text="**[Hawkeye — REVIEW cycle 1]**\n..."
```

---

## Deliverable

Write review report to `services/proposal-generator/pipeline/ADO2732-REVIEW-REPORT.md` with:
1. CC invocation command used
2. Verdict: PASS / NEEDS-CHANGES
3. Each fix: verified ✅ or issue found ❌ with exact XML evidence
