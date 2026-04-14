# Build Report — ADO #1878 + #1879 + #1880

**Commit:** `e1a44e5`
**Branch:** main
**Build:** `dotnet build` → 0 errors, 1 pre-existing warning (CS8601, `FileStorageService.cs` — unrelated)

---

## What was built

Three tightly-coupled bugs in the NEXUS discovery/wizard flow fixed in one build:

- **#1878** — `_hasChanges` now correctly triggers on answered discovery in resume mode; `BuildSpecContextAsync` uses the more reliable `SkippedByUser` flag instead of status enum check.
- **#1879** — Silent re-discovery redirect in `HandleSubmit` replaced with a user-facing confirmation dialog + extracted `ConfirmRediscovery()` method.
- **#1880** — `DiscoveryStep` render guard tightened so `QuestionsReady`/`Answered` sessions always render their question cards; only truly empty/failed sessions show the fallback alert.

---

## Files changed

| File | What changed |
|------|-------------|
| `Components/Pages/NewSpecWizard.razor` | `_hasChanges` + Answered check; `_showRediscoveryConfirm` field; `HandleSubmit` confirm gate; `ConfirmRediscovery()` method; MudAlert confirmation markup in Step 3 |
| `Services/Discovery/DiscoveryService.cs` | `BuildSpecContextAsync` now checks `session.SkippedByUser` instead of `session.Status == DiscoverySessionStatus.Skipped` |
| `Components/Nexus/DiscoveryStep.razor` | Render guard updated to exclude `QuestionsReady` and `Answered` from fallback branch; added `Session.SkippedByUser` check |

---

## Parallelization

No — all three changes touch `NewSpecWizard.razor`; sequential single CC Opus run.

## CC sessions

1 session — CC Opus via pipe mode.

---

## Acceptance criteria

- [ ] **#1878a** — Resume with answered discovery + no file/narrative changes shows "Changes detected" and triggers spec regen — `_hasChanges` now returns true when `_discoverySession.Status == Answered`
- [ ] **#1878b** — `BuildSpecContextAsync` returns empty string for SkippedByUser sessions regardless of status enum value
- [ ] **#1879** — Adding a file in resume mode and clicking Submit shows the warning MudAlert; user must explicitly click "Re-run Discovery" to proceed, or Cancel to stay on step 3
- [ ] **#1880** — After ConfirmRediscovery() redirects to step 2 and poll completes with QuestionsReady, question cards render (not the "Couldn't generate questions" fallback)

---

## Things Clint should scrutinize

1. **`ConfirmRediscovery()` scope** — Contains `await ApplyResumeChangesAsync()` which is also called in the `_regenPending = true` second-pass path. Verify no double-application of file deletions if user goes through the confirm flow.
2. **`_showRediscoveryConfirm` reset** — The flag is reset to `false` inside `ConfirmRediscovery()` and also via the Cancel button inline lambda. Verify it's also reset if user navigates Back from step 3 (BackToStep2Discovery doesn't reset it — consider whether that matters).
3. **`DiscoveryStep.razor` SkippedByUser branch** — The render guard now short-circuits on `Session.SkippedByUser` before checking questions. This means a SkippedByUser session that also has questions will show the "Couldn't generate" fallback (correct intent per spec, but worth confirming with Fred).

---

## How to test locally

```bash
cd ~/projects/fip/nexus && docker compose up nexus-local
```

1. Create a new submission, complete discovery, save answers
2. Resume the submission — verify "Changes detected" alert appears on step 3 review
3. Add a new file in step 1 during resume, advance to step 3, click Submit — confirm the rediscovery warning dialog appears
4. Click "Re-run Discovery" — confirm redirect to step 2 and questions render (not blank)
5. Complete new discovery, return to step 3, Submit — spec regen should fire
