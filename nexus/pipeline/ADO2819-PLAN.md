# BUILD Plan — ADO#2819
## Spec Review UI: NexusAdmin/NexusReviewer read, edit inline, approve spec

**WI:** ADO#2819 | Feature #2815 | Epic #2793  
**Repo:** `/home/fredw/projects/fip/nexus/`  
**Spec Ref:** `nexus-decomp-upgrade-spec-2026-04-27.md` §13

---

## Context

`NexusReview.razor` already exists at `/nexus/{id}/review` with:
- Side-by-side AI Original | Editable panel layout
- Raw markdown textarea for editing (Approved users see read-only after approve)
- Save Draft → `SpecService.SaveDraftAsync`
- Approve → `SpecService.ApproveAsync` (NexusAdmin only via `AuthorizeView Roles="NexusAdmin"`)
- Skeleton loading, error handling

### What needs to change

1. **Role guard for page access** — Currently `[Authorize]` only. Add access check: caller must be NexusAdmin, NexusReviewer, OR the submission owner (SubmittedBy == caller UPN). Non-authorized callers → redirect to `/nexus` with snackbar "Access denied."

2. **Approve button role** — Currently `AuthorizeView Roles="@NexusRoles.Admin"`. Keep NexusAdmin only for Approve. Do NOT add NexusReviewer to Approve — reviewers can edit but only admins approve.

3. **NexusAdmin cross-user access** — `GetByIdAsync` is called without admin flag. Update to pass `isAdmin` so admins can load any submission. Use `UserContextService.IsAdminAsync()`.

4. **Section-by-section inline editing** — Replace the raw `MudTextField Lines="30"` textarea with section-level inline editing:
   - Parse the spec markdown into named sections (by `##` headings)
   - Render each section as readable markdown with an "Edit" icon button
   - Clicking Edit on a section reveals a `MudTextField` for just that section's content
   - On blur/Save, persist via `SpecService.SaveDraftAsync` (reassemble full content from sections)
   - If the spec has no `##` headings, fall back to full-content editor (graceful degradation)

5. **Status display** — Show current `_submission.Status` chip in the header (already rendered in SubmissionDetail but not in NexusReview). Add a status chip near the title.

---

## Acceptance Criteria (all must pass)

- [ ] NexusAdmin AND NexusReviewer can load `/nexus/{id}/review` for any submission
- [ ] NexusUser (submitter) can read their own submission's review page but cannot approve
- [ ] NexusUser who is NOT the submitter → redirect to /nexus with "Access denied"
- [ ] Spec content is displayed section-by-section with per-section Edit buttons
- [ ] Editing a section and blurring persists via SaveDraftAsync; "Saved HH:MM" label updates
- [ ] If no `##` headings found, fall back to single full-content editor (existing behavior)
- [ ] Approve button still restricted to NexusAdmin only
- [ ] NexusAdmin cross-user load works (isAdmin flag passed to service)
- [ ] No regressions on existing Save Draft / Approve flow

---

## Files to change

- `src/FortressNexus.Web/Components/Pages/NexusReview.razor` — main changes
- No new files unless you add a Razor component for the section editor widget

---

## Key types

```csharp
// UserContextService
Task<string> GetUpnAsync()
Task<bool> IsAdminAsync()
Task<bool> IsNexusEditorAsync()  // returns Admin || Reviewer — added by ADO#2821

// SubmissionService
Task<Submission?> GetByIdAsync(int id)  // loads SpecDocuments collection
Task<List<Submission>> GetByUserAsync(string upn, bool isAdmin = false)

// SpecService  
Task SaveDraftAsync(int specDocId, string content, string upn)
Task<SpecDocument> ApproveAsync(int specDocId, ClaimsPrincipal user)

// NexusRoles
const string Admin = "NexusAdmin"
const string Reviewer = "NexusReviewer"
```

---

## CC env vars
```bash
export CLAUDE_CODE_ENTRYPOINT=ado-pipeline
export CLAUDE_CODE_DISABLE_AUTO_MEMORY=1
export CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1
export CLAUDE_CODE_GLOB_TIMEOUT_SECONDS=30
```

## ADO Comment format
```
**[Tony Stark — BUILD cycle 1]**
Commit {hash}: {summary}. Build: SUCCEEDED.
```
`mcporter call devops.add_comment project=Fortress id=2819 text="..."`
