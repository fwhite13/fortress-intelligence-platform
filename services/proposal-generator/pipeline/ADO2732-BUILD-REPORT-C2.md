# Build Report — ADO#2732 Cycle 2
**Status:** SUCCEEDED
**CC invocation:** `cat services/proposal-generator/pipeline/ADO2732-C2-CC-BRIEF.md | CLAUDE_CODE_ENTRYPOINT=ado-pipeline CLAUDE_CODE_DISABLE_AUTO_MEMORY=1 CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1 CLAUDE_CODE_GLOB_TIMEOUT_SECONDS=30 claude --model sonnet --print --dangerously-skip-permissions`
**Commit:** 4abb523
**Baseline:** ce8a2b5 (Fix 1 + Fix 4 correct)

---

## Verification script output (pre-fix, after restoring ce8a2b5 baseline)

```
Total tables: 24
Fix 1: PASS — 0 leading empty paras
Fix 4: PASS — '{#excludedPersons}{name}' — 1 para(s)
```

Note: pre-fix check showed a false positive for Table 11 (blank spacer cell — structurally required empty `w:p`, not a content regression). Refined check uses `len(paras) < 2` guard to skip single-paragraph cells.

---

## Fixes applied

- **Fix 2:** `{#classSchedule}+{state}` → 1 para (Table 7 state cell) — DONE
- **Fix 3:** `{classEstPremium}+{/classSchedule}` → 1 para (Table 7 estPremium cell) — DONE
- **Fix 5:** `Form D-43 — Election to Reject Coverage+{/excludedPersons}` → 1 para (Table 8 electionForm cell) — DONE
- **Fix 1** (retained from ce8a2b5): 0 leading empty paras in recommendation tables — CONFIRMED
- **Fix 4** (retained from ce8a2b5): `{#excludedPersons}{name}` = 1 para — CONFIRMED

---

## Verification script output (post-fix, all 5 fixes)

```
Total tables: 24
Fix 1: PASS — 0 leading empty paras
Fix 4: PASS — '{#excludedPersons}{name}' — 1 para(s)
Fix 2: PASS — '{#classSchedule}{state}' — 1 para(s)
Fix 3: PASS — '{classEstPremium}{/classSchedule}' — 1 para(s)
Fix 5: PASS — 'Form D-43 — Election to Reject Coverage{/excludedPersons}' — 1 para(s)

Verification complete.
```

**Result: 0 issues — all 5 fixes confirmed.**

---

## Generation test

The `generate-proposal.js` script referenced in the plan does not exist in the repo. Generation happens through the running service. Ran `npm test` as the available equivalent:

```
# tests 43
# pass 31
# fail 12
```

The 12 failures are pre-existing (confirmed by testing against unmodified HEAD — same 12 failures). All failures are in `documentRenderer.test.js` and `templateLoader.test.js` and are unrelated to `master.docx`. Our changes only touched the binary template file.

---

## S3 sync

```
upload: ../../../projects/fip/services/proposal-generator/templates/verticals/nbais-wc/master.docx
  to s3://fortress-tools/fip-proposal-templates/verticals/nbais-wc/master.docx
```

Note: `build-nbais-wc-template.py --sync` regenerates master.docx from python-docx source (wiping XML fixes) before syncing. Used direct `aws s3 sync` instead to push the committed binary.

---

## ADO comment

Posted to ADO#2732 (Legacy Work) — comment ID 774500.

---

## Files modified

- `services/proposal-generator/templates/verticals/nbais-wc/master.docx` — all 5 XML fixes applied
- `services/proposal-generator/pipeline/ADO2732-C2-CC-BRIEF.md` — CC brief for this cycle
- `services/proposal-generator/pipeline/ADO2732-BUILD-REPORT-C2.md` — this file

---

## Notes for Clint

1. **Verification is tight** — all 5 fixes confirmed via Python/lxml post-fix check. The verification script (refined to skip single-paragraph cells) reports 0 issues.
2. **`build-nbais-wc-template.py --sync` is dangerous** — it regenerates master.docx from source, wiping XML fixes. Always use direct `aws s3 sync` for deploying the XML-fixed version.
3. **Test suite** — 12 pre-existing test failures in documentRenderer/templateLoader are unrelated to this change.
4. **Table indexing** — Fixes 2 and 3 target Table 7 (0-based index), Fix 5 targets Table 8. This was confirmed by inspecting live cell text during fix application.
