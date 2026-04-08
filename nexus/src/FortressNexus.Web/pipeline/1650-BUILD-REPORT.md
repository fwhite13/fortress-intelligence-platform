# Build Report — WIs #1650, #1651, #1652, #1658

**Batch:** Draft State UI enhancements — all modify `SubmissionDetail.razor`  
**Commit:** `936f3b3`  
**Date:** 2026-04-08  
**Builder:** Tony Stark (software-engineer)

---

## CC Invocation

```bash
cd ~/projects/fip/nexus/src/FortressNexus.Web && \
  cat /tmp/tony-1650-brief.md | claude --model sonnet --print --dangerously-skip-permissions
```

Single CC session covering all four WIs. Build clean, committed, pushed as `936f3b3`.

---

## Cascade Delete Finding (WI #1651)

Verified in `NexusDbContext.cs`:

| Relationship | Cascade Config | Action Needed |
|---|---|---|
| `Submission → SubmissionFiles` | **Cascade** | Auto (none) |
| `Submission → DiscoverySessions` | **Cascade** | Auto (none) |
| `DiscoverySession → DiscoveryQuestions` | **Cascade** | Auto (none) |
| `DiscoveryQuestion → DiscoveryAnswer` | **Cascade** | Auto (none) |
| `Submission → SpecDocuments` | **Restrict** | Explicit delete required |
| `SpecDocument → ArtifactSets` | **Restrict** | Explicit delete required |
| `ArtifactSet → WorkItemRecords` | **Restrict** | Explicit delete required |
| `UploadedFile → SubmissionFiles` | **Restrict** | Must delete SubmissionFiles first (handled by Submission cascade) |

**Deletion sequence in `SubmissionService.DeleteSubmissionAsync`:**
1. S3 object cleanup (non-fatal, best-effort)
2. Clear `ActiveSpecDocumentId` (avoids FK violation on SpecDocument delete)
3. Explicit: `WorkItemRecords → ArtifactSets → SpecDocuments` → `SaveChanges()`
4. Delete `Submission` → cascades `SubmissionFiles` + `DiscoverySessions` (Q&A) → `SaveChanges()`
5. Delete `UploadedFile` records (junction already gone) → `SaveChanges()`

---

## Files Modified

| File | Change |
|---|---|
| `Components/Pages/SubmissionDetail.razor` | All four WIs: narrative preview, Continue CTA, Delete button + confirm, Version History accordion, Discovery history toggle |
| `Services/IDiscoveryService.cs` | Added `GetAllSessionsAsync(int submissionId, CancellationToken ct)` |
| `Services/Discovery/DiscoveryService.cs` | Implemented `GetAllSessionsAsync` — returns all sessions (including Superseded), ordered by `CreatedAt` desc, includes Q&A |
| `Services/ISubmissionService.cs` | Added `DeleteSubmissionAsync(int id)` |
| `Services/SubmissionService.cs` | Implemented `DeleteSubmissionAsync` — 3-phase delete with correct FK ordering |

---

## WI-by-WI Acceptance Criteria

### WI #1650 — Continue Submission CTA + Narrative Preview
- [x] "Continue Submission" `MudButton` (Color.Primary, Variant.Filled) in actions area — only renders when `Status == Draft`
- [x] Navigates to `/nexus/{Id}/resume`
- [x] "Your Narrative" read-only section (MudPaper + MudText) shown for `Status == Draft` + non-empty NarrativeText
- [x] Files list confirmed present (existing Phase 1 feature, no changes)
- [x] Previous version panel (MudExpansionPanel labelled "Previous Version") shown during `Status == Generating` when `SpecDocuments.Count > 1`

### WI #1651 — Delete Submission
- [x] "Delete Submission" button (Color.Error, Variant.Outlined) — only renders for `Status == Draft && (SubmittedBy == currentUserUpn || isAdmin)`
- [x] `IsAdminAsync()` from `UserContextService` used for admin check
- [x] `MudMessageBox` confirmation with exact text: "This is permanent — all files and any generated spec will be deleted. Are you sure?" with Cancel / Delete buttons
- [x] Hard delete implemented in `SubmissionService.DeleteSubmissionAsync` with correct cascade ordering
- [x] Post-delete: navigates to `/nexus` with "Submission deleted." snackbar

### WI #1652 — Version History accordion
- [x] `MudExpansionPanels` shown when `_historicalSpecs.Count > 0` (i.e., `SpecDocuments.Count > 1`)
- [x] Collapsed by default (MudExpansionPanels default)
- [x] Each entry labelled: `"v{N} — {GeneratedAt:yyyy-MM-dd}"`
- [x] Latest version excluded from accordion (`.Skip(1)` on descending-ordered list)
- [x] Truncated content preview (first 200 chars) shown per entry

### WI #1658 — Discovery History toggle
- [x] `MudSwitch` "Show history" toggle shown when `_allDiscoverySessions.Count > 1`
- [x] Visible to all authenticated users (no admin gate)
- [x] When toggled on: all sessions shown, superseded sessions labelled `"Superseded — {CreatedAt:yyyy-MM-dd}"`
- [x] When toggled off (default): only active session shown via `DiscoveryAnswersSummary`
- [x] `_showDiscoveryHistory` bool toggle implemented
- [x] `GetAllSessionsAsync` added to `IDiscoveryService` / `DiscoveryService`

---

## Build Result

```
dotnet build src/FortressNexus.Web/ → 0 errors, 0 warnings
Commit: 936f3b3
```

---

## Known Edge Cases / Clint Should Check

1. **DeleteSubmissionAsync — concurrent session:** If a background spec generation is running when delete fires, the generation may hit a deleted submission. The generation service uses its own DB context scope and will throw/log. This is acceptable for Draft-only deletes (Draft submissions don't have in-flight generation), but worth confirming.

2. **Previous version panel (WI #1650 Generating):** The prev version panel shows full spec content inside a `MudAlert`. For very long specs this could be unwieldy — consider a ScrollArea or maxHeight in a follow-up.

3. **_allDiscoverySessions load path:** Sessions are only loaded when `_submission.DiscoveryStatus` is non-null. If a submission somehow has sessions but no DiscoveryStatus set, the history toggle won't appear. This matches existing behavior for `_discoverySession`.

4. **Delete button visibility:** Uses `_currentUserUpn` compared to `submission.SubmittedBy`. Both store UPN/email. Confirm these use the same format (preferred_username claim) in production Cognito config.

---

## How to Test Locally

1. Create a new submission (Status → Draft)
2. On SubmissionDetail: verify "Continue Submission" button visible, narrative preview shown, "Delete Submission" button visible
3. Click Delete → confirm dialog appears → confirm → redirects to /nexus with toast
4. Create submission with multiple spec versions → verify Version History accordion appears, latest not in accordion
5. Create submission with multiple discovery sessions → "Show history" toggle appears → toggle on shows superseded sessions with labels
