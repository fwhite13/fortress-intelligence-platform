# Build Report — WI #1660: Skip-regen path: Draft → PendingReview direct

**Date:** 2026-04-08  
**Commit:** `137ea83`  
**Branch:** main  
**Status:** ✅ Build passed, 0 errors

---

## What was built

Implemented the skip-regen path in `HandleSubmit` in `NewSpecWizard.razor`. When a user resumes a Draft that already has a `SpecDocument` and makes no changes, the wizard now promotes status to `AwaitingReview` directly and navigates to the submission detail page — no Discovery, no `GenerateAsync`.

---

## CC Invocation

```bash
cd /home/fredw/projects/fip/nexus && cat /tmp/tony-brief-1660.md | claude --model sonnet --print --dangerously-skip-permissions
```

Result: Build succeeded. 1 file changed, 9 insertions(+), 1 deletion(-).

---

## Status Update — How it's done

**Used existing method:** `ISubmissionService.UpdateStatusAsync(int id, SubmissionStatus status)` — no new methods added.

**Status constant:** `SubmissionStatus.AwaitingReview` — this is the codebase's "ready for review" state. The task spec called it "PendingReview" but the enum uses `AwaitingReview`. No `PendingReview` value exists; `AwaitingReview` is correct.

---

## HandleSubmit — 3-way branch structure

```
HandleSubmit()
├── [1] _isResume && _hasChanges
│       └── REGEN PATH (unchanged from WI #1657)
│           ├── FIRST PASS (_regenPending == false):
│           │     Supersede prior discovery, kick off new discovery, set _regenPending=true, return to Step 2
│           └── SECOND PASS (_regenPending == true):
│                 UpdateStatus → Generating, GenerateAsync (sets AwaitingReview internally), navigate
│
├── [2] _isResume && !_hasChanges && _existingSpecDocument != null   ← NEW (WI #1660)
│       └── SKIP-REGEN PATH:
│             UpdateStatusAsync → AwaitingReview
│             Navigate to /nexus/{ResumeSubmissionId}
│             return
│
└── [3] else (new submission OR resume with no changes but no prior spec)
        └── NORMAL FLOW:
              Navigate to /nexus/{_submissionId.Value}
              (spec generation happens async after status update by existing pipeline)
```

---

## Files changed

- `nexus/src/FortressNexus.Web/Components/Pages/NewSpecWizard.razor` — Added 8 lines (skip-regen branch + comment); expanded 1-line fallthrough into explicit branch + fallthrough

---

## Build result

```
dotnet build src/FortressNexus.Web/FortressNexus.Web.csproj
Build succeeded.
0 Error(s)
```

---

## Constraints verified

- ✅ New-submission flow unchanged
- ✅ `_isResume && _hasChanges` (regen) path unchanged
- ✅ No raw strings — used `SubmissionStatus.AwaitingReview`
- ✅ No new service methods
- ✅ Edge case handled: `_isResume && !_hasChanges && _existingSpecDocument == null` falls through to normal flow

---

## Known edge cases / things Clint should scrutinize

1. **`AwaitingReview` vs `PendingReview`** — The task spec said "PendingReview" but the enum uses `AwaitingReview`. That's what the regen path uses post-`GenerateAsync` too, so this is consistent. Confirm intent.
2. **Navigation target** — Skip-regen uses `ResumeSubmissionId` (the route param); new-submission fallthrough uses `_submissionId.Value`. Both should resolve to the same ID in resume mode, but worth double-checking at runtime.
3. **Status transition guard** — No guard prevents calling `UpdateStatusAsync` if the submission is somehow already `AwaitingReview` on entry. Low risk for Draft resumes but worth noting.

---

## How to test locally

1. Create a submission → complete spec gen → submission is `AwaitingReview`
2. (Manually set back to `Draft` in DB for testing)
3. Resume the submission — make NO changes
4. Submit → should skip Discovery, set status to `AwaitingReview`, navigate to `/nexus/{id}`
5. Confirm no Discovery session initiated, no new spec document created

Also test: resume Draft with no prior spec + no changes → should fall through to normal Discovery flow.
