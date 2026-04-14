# Build Report — ADO #1882

**Commit:** `7f1c3d5`
**Branch:** main
**Build:** dotnet build → 0 errors (1 pre-existing warning in FileStorageService.cs — unrelated)

## Files changed
| File | Change |
|------|--------|
| `nexus/src/FortressNexus.Web/Components/Pages/NewSpecWizard.razor` | Terminal session supersede guard in `GoToStep2Discovery` (lines 459–499) |

## Root cause
When a user resumes a submission that already has a `Skipped` or `Failed` discovery session, `LoadSubmissionAsync` loads that session into `_discoverySession` (non-null). The `GoToStep2Discovery()` guard `if (_discoverySession == null)` evaluates false, so `InitiateDiscoveryAsync` never fires. The stale session is displayed to `DiscoveryStep`, which sees `SkippedByUser == true`, renders the fallback UI, and when the user clicks Continue, `HandleSkip()` re-writes `status=Skipped, skipped_by_user=1` on the already-skipped session. No new session is ever created.

## Fix applied
Inserted a terminal-session supersede block immediately before the existing `if (_discoverySession == null)` check (lines 459–468). When `_discoverySession` is non-null and its status is `Skipped`, `Failed`, or `Superseded`, the code calls `DiscoveryService.SupersedeSessionAsync()` to archive the stale session (same call used in `ConfirmRediscovery`), sets `_discoverySession = null`, and falls through into the existing `InitiateDiscoveryAsync` + 60-second poll loop. The `else` comment was updated to clarify which statuses legitimately bypass re-initiation (`Pending`, `QuestionsReady`, `Answered`).

## CC invocation
`cat /tmp/tony-1882-brief.md | claude --model sonnet --print --dangerously-skip-permissions`

## Self-review checklist
- [x] Skipped/Failed/Superseded sessions are superseded before re-initiation
- [x] `_discoverySession` set to null after supersede
- [x] `InitiateDiscoveryAsync` fires on null `_discoverySession` (new AND re-initiation paths)
- [x] QuestionsReady/Answered/Pending sessions untouched (else branch unchanged)
- [x] No changes to `DiscoveryService.cs`
- [x] `dotnet build` → 0 errors
