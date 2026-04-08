# Review Report — WI #1662 — NEXUS Phase 3 Schema Anchor Migration

**Reviewer:** Hawkeye (Clint Barton)  
**Date:** 2026-04-08  
**Cycle:** 1 of 2  
**Commit:** `d42d0ed`  
**Risk:** Medium  

---

## Verdict: NEEDS-CHANGES

---

## CC Review Summary

CC read all 8 target files plus grepped the full codebase for status literals and banned patterns. Analysis was thorough. One real finding confirmed. No false positives identified — all other checks came back clean.

---

## Spec Compliance Check

**§ Files Modified:**
- `Models/Enums/DiscoverySessionStatus.cs` — ✅ created as specified
- `Migrations/20260408162324_AddPhase3ResumeChanges.cs` — ✅ created, all 5 columns covered
- `Migrations/20260408162324_AddPhase3ResumeChanges.Designer.cs` — ✅ correct migration ID, correct DbContext ref
- `Migrations/NexusDbContextModelSnapshot.cs` — ✅ updated, consistent with post-migration state
- `Data/NexusDbContextDesignTimeFactory.cs` — ✅ created, implements correct interface

**§ Out of Scope:** No out-of-scope changes detected ✅

**Spec compliance verdict:** ✅ COMPLIANT (except the constants adoption gap — see I1 below)

---

## Consistency Audit

**Files Cross-Referenced:**
- `DiscoverySessionStatus.cs` ↔ `DiscoveryService.cs` — ✅ all values match, ❗ 13 raw literals not using constants
- `DiscoverySessionStatus.cs` ↔ `NewSpecWizard.razor` — ✅ values match, ❗ 2 raw literals not using constants
- `DiscoverySessionStatus.cs` ↔ `DiscoveryAnswersSummary.razor` — ✅ value matches, ❗ 1 raw literal not using constants
- `NexusDbContext.cs` OnModelCreating ↔ migration ↔ snapshot — ✅ fully consistent
- Designer.cs migration ID ↔ filename — ✅ exact match

**HasColumnType in NexusDbContext.cs:** 0 matches ✅  
**HasConversion on Guid properties:** 0 matches ✅  
**Total status literals grep'd:** 17 occurrences across 3 files — all values correct today, none using constants

---

## Critical Issues — 0

None.

---

## Important Issues — 1

### I1: Constants Class Defined but Unadopted — All Callers Still Using Raw Literals

**Files:** `Services/Discovery/DiscoveryService.cs`, `Components/.../NewSpecWizard.razor`, `Components/.../DiscoveryAnswersSummary.razor`  
**Category:** Correctness / Consistency  

**Issue:**  
`DiscoverySessionStatus.cs` was created to replace magic strings, but every single caller still uses raw string literals. The constants class provides zero compile-time safety until callers migrate to it. If any constant value is renamed or corrected in the future, all 17 call sites will silently break — no compiler error, no warning.

**Today's values all match** — there are no wrong strings in any caller. But the purpose of the constants class is to make that guarantee structural, not coincidental.

**Affected literals (all correct today, all should be constants):**

| File | Line(s) | Literal | Constant |
|------|---------|---------|----------|
| `DiscoveryService.cs` | 53, 63 | `"Pending"` | `DiscoverySessionStatus.Pending` |
| `DiscoveryService.cs` | 133, 137 | `"Answered"` | `DiscoverySessionStatus.Answered` |
| `DiscoveryService.cs` | 154, 158, 167 | `"Skipped"` | `DiscoverySessionStatus.Skipped` |
| `DiscoveryService.cs` | 277, 279, 344, 346 | `"Failed"` | `DiscoverySessionStatus.Failed` |
| `DiscoveryService.cs` | 330, 336 | `"QuestionsReady"` | `DiscoverySessionStatus.QuestionsReady` |
| `NewSpecWizard.razor` | 289 | `"QuestionsReady"`, `"Failed"` | see fix below |
| `DiscoveryAnswersSummary.razor` | 7 | `"Skipped"` | `DiscoverySessionStatus.Skipped` |

**Fix — DiscoveryService.cs** (example; apply to all 13 occurrences):
```diff
- session.Status = "Pending";
+ session.Status = DiscoverySessionStatus.Pending;
```

**Fix — NewSpecWizard.razor** (C# `is` pattern with string constants):
```diff
- if (_discoverySession?.Status is "QuestionsReady" or "Failed") break;
+ if (_discoverySession?.Status is DiscoverySessionStatus.QuestionsReady or DiscoverySessionStatus.Failed) break;
```

**Fix — DiscoveryAnswersSummary.razor:**
```diff
- else if (Session.Status == "Skipped" || Session.SkippedByUser)
+ else if (Session.Status == DiscoverySessionStatus.Skipped || Session.SkippedByUser)
```

Add `@using FortressNexus.Web.Models.Enums` to razor files (or add to `_Imports.razor` if not already present).

---

## Nitpicks — 1

**N1:** `DiscoverySessionStatus.Superseded` is defined but has zero usages anywhere in the codebase. This is fine for forward-compatibility stubs, but worth noting in case it was accidentally omitted from the original spec or not yet wired up.

---

## Positive Observations

- **Migration is excellent.** All 5 FK/ID columns hit, both FK relationship ends covered atomically (session↔question, question↔answer), `Down()` reversal is complete and correct. No raw SQL. No FK constraint drama — `char(36)` ↔ `varchar(36)` is a safe in-place MySQL 8 modification.
- **Pomelo compliance is clean.** Zero `HasColumnType` or `HasConversion` violations in `OnModelCreating` for Discovery entities. The banned patterns are not present anywhere in the file.
- **Design-time factory is safe.** Hardcoded localhost dummy string — absolutely no path to production Aurora. Correct interface implementation. Not DI-registered.
- **Snapshot is consistent.** All 5 columns show `char(36)` in the snapshot, matching the post-migration state exactly. Migration ID in Designer.cs matches filename.
- **Constants class is well-formed.** Static class (not enum), correct namespace, 6 values, no typos, Pascal case matches string values.

---

## Check-by-Check Summary

| Check | Verdict | Notes |
|-------|---------|-------|
| 1. Migration SQL correctness | ✅ PASS | All 5 columns, correct collation, FK coverage, Down() clean |
| 2. Pomelo convention compliance | ✅ PASS | 0 HasColumnType, 0 HasConversion on Guid props |
| 3. DiscoverySessionStatus constants | ⚠️ NEEDS-CHANGES | Constants correct, but unadopted by all callers |
| 4. Design-time factory | ✅ PASS | Localhost dummy, no production exposure |
| 5. Migration idempotency | ✅ PASS | EF owns history table, Migration ID correct |
| 6. Model snapshot consistency | ✅ PASS | Snapshot matches post-migration state |

---

## What Tony Needs to Fix

**Single required change before PASS:**

Replace all 17 raw status string literals in `DiscoveryService.cs`, `NewSpecWizard.razor`, and `DiscoveryAnswersSummary.razor` with `DiscoverySessionStatus.*` constant references. Add the namespace import where needed.

No other changes required. The migration, Pomelo config, factory, and snapshot are all production-ready as-is.

---

*Reviewed by Hawkeye using Claude Code CLI (CC Sonnet) — adversarial review spec at `/tmp/clint-brief-1662.md`*

---

## Review Report — Cycle 2

**Commit reviewed:** `90fa325`
**Date:** 2026-04-08
**Reviewer:** Hawkeye (code-reviewer)
**Cycle:** 2 of 2 — Verification of I1 fix

### Verdict: ✅ PASS

---

### What Was Verified

Cycle 1 issued NEEDS-CHANGES on a single issue (I1): all 17 raw status string literals in `DiscoveryService.cs`, `NewSpecWizard.razor`, and `DiscoveryAnswersSummary.razor` needed to be replaced with `DiscoverySessionStatus.*` constants. Tony fixed this in commit `90fa325`.

---

### Task-by-Task Results

| Task | Check | Result |
|------|-------|--------|
| 1 | Raw string literals remaining | ✅ PASS — Zero remaining across all 3 files |
| 2 | Constant references correct (no typos) | ✅ PASS — All 16 usages verified correct |
| 3 | Diff scope (only expected files changed) | ✅ PASS — 3 source files + 3 pipeline artifacts |
| 4 | `_Imports.razor` has `@using FortressNexus.Web.Models.Enums` | ✅ PASS — Found at Components/_Imports.razor:18 |
| 5 | `dotnet build` clean | ✅ PASS — 0 errors, 0 warnings |

---

### Constant References Verified

**DiscoveryService.cs** — 13 usages (+ `using FortressNexus.Web.Models.Enums;` at line 8):
- L54, L64: `DiscoverySessionStatus.Pending` ✓
- L134, L138: `DiscoverySessionStatus.Answered` ✓
- L155, L159, L168: `DiscoverySessionStatus.Skipped` ✓
- L278, L280, L345, L347: `DiscoverySessionStatus.Failed` ✓
- L331, L337: `DiscoverySessionStatus.QuestionsReady` ✓

**NewSpecWizard.razor** — 2 usages (L289):
- `DiscoverySessionStatus.QuestionsReady` ✓
- `DiscoverySessionStatus.Failed` ✓

**DiscoveryAnswersSummary.razor** — 1 usage:
- L7: `DiscoverySessionStatus.Skipped` ✓

---

### Scope Check

Diff `d42d0ed..90fa325` touched 6 files:
- 3 source files — exactly as expected
- 3 pipeline artifacts (`1662-BUILD-REPORT.md`, `1662-REVIEW-REPORT.md`, `P3-STATE.md`) — benign, no source impact

No scope creep.

---

### I1 Resolution Confirmed

All 17 literals replaced. No raw strings remain. Build is clean. Namespace resolution confirmed via `_Imports.razor`. I1 is fully resolved.

---

### Final Check-by-Check Summary

| Check | Verdict |
|-------|---------|
| 1. Zero raw literals remaining | ✅ PASS |
| 2. Constant references correct | ✅ PASS |
| 3. Build clean | ✅ PASS |
| 4. Diff scope clean | ✅ PASS |
| 5. `_Imports.razor` namespace confirmed | ✅ PASS |

---

*Reviewed by Hawkeye using Claude Code CLI (CC Sonnet) — adversarial review spec at `/tmp/clint-brief-1662-c2.md`*

---

## Review Report — Cycle 3

**Commit reviewed:** `109cf13`
**Date:** 2026-04-08
**Reviewer:** Hawkeye (code-reviewer)
**Cycle:** 3 — Targeted FK fix review (migration deploy failure)

### Verdict: ✅ PASS

---

### Background

Cycle 2 issued PASS on the migration content, but the migration failed on MySQL deploy:

> `ER_FK_INCOMPATIBLE_COLUMNS`: MySQL rejected `MODIFY COLUMN` on `discovery_sessions.id` because `discovery_questions.discovery_session_id` had a FK referencing it with an incompatible type.

Tony's fix: wrap the 5 `AlterColumn` calls in `Up()` and `Down()` with `DropForeignKey` before and `AddForeignKey` after for the two FKs that reference the columns being altered.

---

### Build

`dotnet build` — **0 errors, 0 warnings** ✅

---

### CC Review Results (all 6 checks)

| Check | Verdict | Notes |
|-------|---------|-------|
| 1. SQL operation order in Up() | ✅ PASS | DropFK×2 → AlterColumn×5 → AddFK×2 — exact required order |
| 2. FK names vs. origin migration | ✅ PASS | Both names match `20260407180206_AddDiscoveryConversation.cs` exactly |
| 3. AddForeignKey params correctness | ✅ PASS | table/column/principalTable/principalColumn/Cascade all correct |
| 4. Down() symmetry | ✅ PASS | DropFK×2 → AlterColumn×5 → AddFK×2 — perfectly symmetric |
| 5. Third FK exclusion justified | ✅ PASS | `FK_discovery_sessions_submissions_submission_id` touches no altered columns |
| 6. Commit scope | ✅ PASS | 1 migration file + 3 pipeline docs — no unexpected source changes |

---

### Check Detail

**Check 1 — Up() operation order:**
1. `DropForeignKey` — `FK_discovery_questions_discovery_sessions_discovery_session_id`
2. `DropForeignKey` — `FK_discovery_answers_discovery_questions_discovery_question_id`
3. `AlterColumn` — `discovery_sessions.id`
4. `AlterColumn` — `discovery_questions.discovery_session_id`
5. `AlterColumn` — `discovery_questions.id`
6. `AlterColumn` — `discovery_answers.discovery_question_id`
7. `AlterColumn` — `discovery_answers.id`
8. `AddForeignKey` — `FK_discovery_questions_discovery_sessions_discovery_session_id`
9. `AddForeignKey` — `FK_discovery_answers_discovery_questions_discovery_question_id`

**Check 2 — FK name match:**
- `FK_discovery_questions_discovery_sessions_discovery_session_id` — exact match ✅
- `FK_discovery_answers_discovery_questions_discovery_question_id` — exact match ✅

**Check 3 — AddForeignKey params:**
- FK#1: `table=discovery_questions`, `column=discovery_session_id`, `principalTable=discovery_sessions`, `principalColumn=id`, `onDelete=Cascade` ✅
- FK#2: `table=discovery_answers`, `column=discovery_question_id`, `principalTable=discovery_questions`, `principalColumn=id`, `onDelete=Cascade` ✅

**Check 4 — Down() sequence:** Identical structure to Up() — DropFK×2 → AlterColumn×5 → AddFK×2. Symmetric. ✅

**Check 5 — Third FK exclusion:** `FK_discovery_sessions_submissions_submission_id` references `submissions.id` (INT principal) via `discovery_sessions.submission_id` (INT FK column). Neither column is in the set being altered. Exclusion is correct. ✅

**Check 6 — Scope:** `20260408162324_AddPhase3ResumeChanges.cs` + 3 pipeline docs. Clean. ✅

---

### Summary

The fix is surgically correct. The two FKs that reference columns being modified are dropped before the alters and re-added after, in both `Up()` and `Down()`. Names match the origin migration exactly. The third FK (submissions) is correctly left untouched. This migration is ready to deploy.

---

*Reviewed by Hawkeye using Claude Code CLI (CC Sonnet) — adversarial review spec at `/tmp/clint-brief-1662-c3.md`*
