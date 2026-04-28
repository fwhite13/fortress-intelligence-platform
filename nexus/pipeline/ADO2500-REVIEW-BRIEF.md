# REVIEW Assignment: ADO#2500

## Task
**NexusArtifacts UI — Test Case grouping, WI template badges, predecessor badges, external dependency panel**
**ADO WI:** https://dev.azure.com/FortressAffinityGroup/4f20ca74-10f3-4707-b00b-04cd4e147909/_workitems/edit/2500
**Review cycle:** 1 of 2

## Spec (MANDATORY — read before reviewing)
`/home/fredw/.openclaw/workspace/memory/projects/nexus-decomp-upgrade-spec-2026-04-27.md`
Focus on **§8 — UI Specification** — wireframes, badge table, panel layout, copy button behavior.

## Files created/modified by Tony (commit `5159377`)

| File | Action |
|------|--------|
| `src/FortressNexus.Web/Components/Pages/NexusArtifacts.razor` | **NEW** — full WI tree page |
| `src/FortressNexus.Web/Controllers/NexusArtifactsController.cs` | **NEW** — external-dependencies endpoint |
| `src/FortressNexus.Web/Components/Pages/SubmissionDetail.razor` | Modified — "View Work Items" nav button added |

## Build result
SUCCEEDED — 0 errors, 1 pre-existing warning.

## Key context
- Tony noted NexusArtifacts.razor is a NEW file (may not have existed before — this is the full WI review page)
- Tony caught and handled MudBlazor v7 API difference: uses `Expanded` not `IsInitiallyExpanded` on `MudExpansionPanel`
- External dependencies list is filtered in-memory from loaded WIs (not a second HTTP fetch) — valid for Phase 1

## Review Focus

### 1. External Dependencies Panel
- Rendered ONLY when `ExternalDependencyCount > 0`? (Hidden entirely when 0)
- Amber `MudAlert` with `Severity.Warning` shows correct message: "⚠️ {N} external dependencies require action before these WIs can be completed"
- Each external WI entry shows: owner name (bold), title, description preview (first 120 chars), tag chips
- "Copy brief" button wires to `IJSRuntime.InvokeVoidAsync("navigator.clipboard.writeText", description)` with FULL description (not preview)
- Panel has a collapse/expand toggle

### 2. WI Template Badges
- Infrastructure → 🏗️ MudChip, teal/Info color, left of WI type badge?
- Migration → 🔄 MudChip, purple/Secondary color?
- Test Case → 🧪 MudChip, blue/Primary color?
- Standard WIs → NO badge?
- Badges appear on ALL WI types that have non-Standard templates (not just User Stories)?

### 3. Predecessor Badges
- Same-Epic: amber/Warning chip "⛓ Blocked by: [truncated title]" with full title tooltip?
- Cross-Epic: orange/Warning chip "⛓ Cross-Epic: [Epic] > [truncated title]" with full title tooltip?
- Unresolved: red/Error chip "⛓ [!] [title]" with tooltip "Could not be auto-linked"?
- Rendered INLINE after WI title (not in a separate section)?
- Null-safe — WIs with no predecessors show nothing?
- How does Tony determine cross-Epic vs same-Epic? Check the helper logic for correctness.
- How does Tony determine unresolved? (A title not matching any WI in the loaded set is correct)

### 4. Test Case Grouping
- Test Cases NOT rendered inline in the main Epic→Feature→Story→Task tree?
- Each User Story node shows "🧪 Test Cases (N)" `MudExpansionPanel` BELOW its Tasks?
- Panel collapsed by default?
- Test Cases matched to parent Story via `ParentTitle == story.Title`?
- Each Test Case entry in the panel shows: title, acceptance criteria content, 🧪 badge?
- "N" in the header shows the correct count?

### 5. Controller endpoint
- `GET /nexus/{id}/artifacts/external-dependencies` exists in `NexusArtifactsController.cs`?
- Auth policy matches the existing artifacts route?
- Returns `WorkItemRecord[]` where `IsExternalDependency = true` from the LATEST `ArtifactSet` for the submission?
- Query uses `OrderByDescending` (or equivalent) to get the latest artifact set?

### 6. SubmissionDetail.razor change
- "View Work Items" button navigates to the correct `/nexus/{id}/artifacts` route?
- No regressions to existing SubmissionDetail functionality?

### 7. MudBlazor v7 compatibility
- Tony used `Expanded` not `IsInitiallyExpanded` — verify this is the correct v7 API for default-collapsed panels
- No other deprecated/missing v7 APIs used?
- Check: `Icons.Material.Outlined.OutboxRounded` does NOT exist in v7 — confirm Tony used `ContentCopy` or similar for the copy button

### 8. Data loading
- How does the page load `ArtifactSet` + `WorkItemRecords`? Are the EF includes correct to load all needed data in one query?
- Is `ExternalDependencyCount` accessible from the page (it's on `ArtifactSet`, not `WorkItemRecord`)?

## MANDATORY: Use Claude Code CLI
```
cat /tmp/review-2500-brief.md | claude --model sonnet --print --dangerously-skip-permissions
```
Review Report MUST include CC invocation. Do NOT reason about markup without CC reading it first.

## Deliverables
1. **Review Report** at `/home/fredw/projects/fip/nexus/pipeline/ADO2500-REVIEW-REPORT.md`
   - Verdict: PASS / NEEDS-CHANGES / FAIL
   - Explicit check on cross-Epic detection logic correctness
   - CC invocation used
2. **ADO comment** on WI #2500:
   ```
   mcporter call devops.add_comment project="FAIT" id=2500 text="**[Hawkeye — REVIEW cycle 1]**
   Code review [PASS/NEEDS-CHANGES]. [summary]"
   ```

## When done
```
openclaw system event --text "ADO2500 REVIEW COMPLETE: [PASS/NEEDS-CHANGES]" --mode now
```
