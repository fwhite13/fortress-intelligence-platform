# Pipeline Completion: WI827

## Outcome: DEPLOYED ✅
**Date:** 2026-03-17
**Total pipeline time:** ~29 minutes (01:33 build → 02:00 confirm)

---

## What Shipped

Sprint 11: Formula Intelligence.

- **`formulaBuilder.ts`** (new) — `FormulaSpec` interface; `ensureScratchSheet()` creates `__FAIT_SCRATCH__` with `Excel.SheetVisibility.veryHidden`; `previewFormula()` writes prefixed formula to scratch, reads value, clears atomically in same `Excel.run`; `writeFormula()` writes original formula to target cell with `setFaitWriting` in `finally`, non-fatal `comments.add()` in split sync; `prefixFormulaRefs()` prefixes cell refs for cross-sheet evaluation; `formatPreviewValue()`.
- **`suggestionParser.ts`** — `formula_spec` JSON block parser; `formulaSpec: FormulaSpec | null` on `ParseResult`.
- **`SlashCommandPicker.tsx`** — `/formula` entry with `__FORMULA_COMMAND__` sentinel.
- **`ChatPanel.tsx`** — Sprint 11 state vars, `handleFormulaGenerate()`, `handleFormulaPreview()`, `handleFormulaWrite()`, `handleFormulaDismiss()`, preview config panel, action bar.
- **`useChat.ts`** — `formulaSpec` on `Message`.

**fred-dev:** `fred-dev:118` | **fait-prod:** `fait-prod:29` | fip commit `8304af3` | Bundle `0jKgr1fV`

---

## Pipeline Summary

| Stage | Status | Notes |
|-------|--------|-------|
| PLAN | ✅ | Spec: SPRINT11-SPEC.md |
| BUILD | ✅ | 1 cycle; commit 4e652d5; 58 modules, 0 TS errors |
| REVIEW C1 | ❌ | NEEDS-CHANGES: comments.add sync boundary; VeryHidden `as any` cast |
| BUILD C2 | ✅ | commit 0671ddc — split sync; `Excel.SheetVisibility.veryHidden` enum |
| REVIEW C2 | ✅ | PASS — 4/4 clean |
| SECURITY | ✅ | PASS — no findings |
| APPROVE | ✅ | Standing approval |
| DEPLOY | ✅ | 1 cycle; CodeBuild SUCCEEDED; fip-tokens.css 200/200 |
| VERIFY | ✅ | Natasha — PASS |
| CONFIRM | ✅ | WI#827 → Done |

**Review cycles:** 2 | **Deploy cycles:** 1 | **Security findings:** None

---

## Lessons / Notes

- **`comments.add()` try/catch must wrap the sync, not just the queue call.** Office JS errors surface at `ctx.sync()` time — a catch around `comments.add()` alone doesn't protect the sync. Fix: split into two syncs, comment in its own try/catch.
- **`Excel.SheetVisibility.veryHidden` enum is typed in `@types/office-js`.** No `as any` needed — the enum is accepted directly.
- **Atomic scratch-cell pattern** is equivalent to (and cleaner than) the spec's `clearScratchCell()` finally approach. Write + read + clear in one `Excel.run` = scratch never persistently dirty.
- `/formula` functional testing MANUAL REQUIRED — needs Excel Online session.
