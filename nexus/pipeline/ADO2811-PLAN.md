# BUILD Assignment: ADO#2811
## NEXUS Admin Cross-User Visibility

**WI:** ADO#2811 | Project: Fortress | Feature: #2816 | Epic: #2793
**Risk:** medium | **Pipeline path:** full
**Spec ref:** `nexus-decomp-upgrade-spec-2026-04-27.md` §13 Admin Cross-User Visibility
**ADO attribution prefix:** `**[Tony Stark — BUILD cycle 1]**`

---

## What to Build

NexusAdmin role should bypass ownership checks across all NEXUS operations, and admins should see the submitter UPN in the list view.

---

## Pre-read

Before coding, read:
1. `/home/fredw/projects/fip/nexus/src/FortressNexus.Web/Services/SubmissionService.cs` — all ownership checks
2. `/home/fredw/projects/fip/nexus/src/FortressNexus.Web/Controllers/NexusArtifactsController.cs` — external-deps endpoint
3. All Razor pages that display submissions — grep for `GetByUserAsync` and `SubmittedBy`
4. `nexus-decomp-upgrade-spec-2026-04-27.md` §13 for full scope

```bash
export CLAUDE_CODE_ENTRYPOINT=ado-pipeline
export CLAUDE_CODE_DISABLE_AUTO_MEMORY=1
export CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1
export CLAUDE_CODE_GLOB_TIMEOUT_SECONDS=30
```

---

## Changes Required

### 1. SubmissionService.cs

**`GetByUserAsync`** — rename or add overload `GetAllOrByUserAsync(userUpn, isAdmin)`:
- If `isAdmin == true`: return ALL submissions (no `.Where(s => s.SubmittedBy == userUpn)` filter)
- If `isAdmin == false`: existing behavior (filter by userUpn)

**All mutating methods** — extend admin bypass to:
- `UpdateSubmissionAsync` (if it has ownership check)
- `DeleteSubmissionAsync` (already has `callerIsAdmin` param — verify it works)
- `ApproveSpecDocumentAsync` or equivalent (if ownership-gated)
- `GetSubmissionAsync` (single-submission fetch — add admin bypass if ownership-checked)
- Any other method with `.SubmittedBy == callerUpn` guard

Pattern to add where missing:
```csharp
if (!callerIsAdmin && submission.SubmittedBy != callerUpn)
    throw new UnauthorizedAccessException("...");
```

### 2. Caller sites — pass `isAdmin` flag

Wherever `SubmissionService` is called from Razor pages or controllers, fetch `isAdmin` from `UserContextService.IsAdminAsync()` and pass it through.

Typical pattern in Razor components:
```csharp
var isAdmin = await UserContextService.IsAdminAsync();
var submissions = await SubmissionService.GetAllOrByUserAsync(userUpn, isAdmin);
```

### 3. NexusArtifactsController — external-deps endpoint

The existing GET `/nexus/{id}/artifacts/external-dependencies` endpoint:
- Currently only checks `[Authorize]`
- Add: if user is not admin, verify they own the submission (same ownership check as other endpoints)
- If admin: allow access to any submission's external deps

### 4. UI — show submitter UPN in admin list view

In the submissions list page (find the Razor component that renders `GetByUserAsync` results):
- If `_isAdmin`, add a "Submitter" column showing `submission.SubmittedBy`
- Non-admins: no change (they only see their own, UPN column not needed)

Use `MudText Typo="Typo.caption"` or a `MudChip` for the UPN display — keep it compact.

---

## Acceptance Criteria

1. NexusAdmin user calling the submissions list endpoint sees ALL submissions (not just their own)
2. NexusAdmin can view, edit, and trigger actions on any user's submission
3. NexusAdmin sees submitter UPN in the list view; non-admins do not see this column
4. NexusAdmin can access external-deps endpoint for any submission
5. Non-admin users still only see/act on their own submissions
6. `DeleteSubmissionAsync` admin bypass continues to work

---

## Build Report Format

```markdown
# Build Report — ADO#2811
## CC Invocation
`cat brief.md | claude --model sonnet --print --dangerously-skip-permissions`
## Changes
- Files modified (list each)
## AC Checklist
1. Admin sees all submissions — [PASS/FAIL]
2. Admin can act on any submission — [PASS/FAIL]
3. Admin sees submitter UPN in list — [PASS/FAIL]
4. Admin can access external-deps for any submission — [PASS/FAIL]
5. Non-admin restricted to own submissions — [PASS/FAIL]
6. Delete admin bypass works — [PASS/FAIL]
## Self-review
- [ ] All ownership checks in SubmissionService patched
- [ ] All caller sites pass isAdmin flag
- [ ] NexusArtifactsController external-deps endpoint patched
- [ ] UI shows Submitter column only for admins
```

---

## ADO Comment

```bash
mcporter call devops.add_comment project=Fortress id=2811 text="**[Tony Stark — BUILD cycle 1]**\nCommit {hash}: Admin bypass extended to GetByUser/GetSubmission/mutating methods; submitter UPN in list view for admins; external-deps endpoint patched. Build: SUCCEEDED."
```

---

## MANDATORY: CC

Working directory: `/home/fredw/projects/fip/nexus/`
Commit message: `feat(ADO#2811): NexusAdmin cross-user visibility and bypass`
