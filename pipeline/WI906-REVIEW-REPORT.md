# Review Report: WI906 — Sprint 6, Cycle 1

**Reviewer:** Hawkeye (Clint Barton)
**Date:** 2026-03-19
**Commits reviewed:** `19a8227` (main build) + `c5cae1f` (PortalName fix)
**HEAD:** `c5cae1f`
**Files:** 24 files changed, 4801 insertions, 27 deletions

---

## Verdict: ✅ PASS

All critical checks passed. No blockers. No important issues. Two nitpicks noted below.

---

## Critical Checks

### ✅ 1. @rendermode — Routes.razor / App.razor (CRITICAL — WI908 lesson)
- `Routes.razor`: **EMPTY** — no `@rendermode` attribute present. Correct.
- `App.razor` line 15: `<Routes @rendermode="InteractiveServer" />` — correct, sole rendermode placement.

### ✅ 2. IMudDialogInstance (MudBlazor v7)
- Zero matches in `Components/`. Clean — v7 `MudDialogProvider` pattern used correctly.

### ✅ 3. Aurora migration — PrimaryContactId (try/catch 1060)
```csharp
try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE opportunities ADD COLUMN PrimaryContactId CHAR(36) NULL"); }
catch (MySqlException ex) when (ex.Number == 1060) { logger.LogDebug("PrimaryContactId column already exists"); }
```
Correct pattern. No `IF NOT EXISTS` on ALTER (Aurora doesn't support it). Try/catch 1060 is right.

### ✅ 4. New tables — CREATE TABLE IF NOT EXISTS
Both `contacts` and `opportunity_documents` use `CREATE TABLE IF NOT EXISTS`. Safe for idempotent re-runs.

### ✅ 5. DESIGN-SYSTEM compliance — new Razor files
- `MudButton Variant=` inline: **0 matches** across all new components.
- `Icons.Material.*` direct: **0 matches** — all icons routed through `FamosIcons.*`.
- `Style="width:..."` on inputs: **0 matches**.
- New icons (`FamosIcons.Upload`, `FamosIcons.Download`, `FamosIcons.Note`, `FamosIcons.NoteAlt`, `FamosIcons.Contacts`, `FamosIcons.Search`) all defined in `FamosIcons.cs` as wrappers around `Icons.Material.Outlined.*` — correct pattern.

### ✅ 6. Contact single-primary constraint (LifecycleCommandService)
`AddContactAsync` enforces constraint:
```csharp
if (contactType == ContactType.Primary && opp.Contacts.Any(c => c.ContactType == ContactType.Primary))
{
    throw new LifecycleValidationException(
        "This opportunity already has a primary contact. ...");
}
```
Properly guarded within a DB transaction. Correct.

### ✅ 7. AssignOwnerAsync — exists and wired
- Defined at `LifecycleCommandService.cs:556`
- Wired in `OpportunityWorkspace.razor:203` — invoked after `OwnerPickerDialog` result

### ✅ 8. AddNoteAsync — exists and wired
- Defined at `LifecycleCommandService.cs:577`
- Wired in `ActivityPanel.razor:76` — called on submit

### ✅ 9. PortalName = "TIG Dashboard"
`appsettings.json`: `"PortalName": "TIG Dashboard"` ✓
Commit `c5cae1f` correctly reverted the stale "Titan Dashboard" value.

### ✅ 10. Scope — only famos/ files in commit
`git show 19a8227 --stat` shows only `famos/src/FamOs.Web/...` paths. Clean scope.

### ✅ 11. AWSSDK.S3 packages — expected for Documents panel
```xml
<PackageReference Include="AWSSDK.S3" Version="3.7.*" />
<PackageReference Include="AWSSDK.Extensions.NETCore.Setup" Version="3.7.*" />
```
Both expected. No unexpected packages added.

### ✅ 12. OpportunityWorkspace.razor — new panels wired
```
line 124: <ContactsPanel  Opportunity="_opp" OnUpdated="Reload" />
line 125: <DocumentsPanel Opportunity="_opp" OnUpdated="Reload" />
line 126: <ActivityPanel  Opportunity="_opp" OnUpdated="Reload" />
line 196: await DialogService.ShowAsync<OwnerPickerDialog>(...)
```
All four wired correctly with consistent `Opportunity` + `OnUpdated` parameter pattern.

---

## Issues

### Nitpicks

**N1 — DocumentService: S3 bucket name hardcoded**
`DocumentService.cs:23`: `private const string BucketName = "fip-cowork-workspaces";`
Consider moving to `IConfiguration` / `appsettings.json` for environment portability. Not a blocker for this sprint.

**N2 — DocumentsPanel: 25MB upload cap — undocumented**
`DocumentsPanel.razor:104`: `const long maxSize = 25 * 1024 * 1024; // 25MB`
No UI text informs the user of the limit before they attempt an upload. A brief caption (e.g., "Max 25MB") on the upload area would prevent silent failures for large files. Non-blocking.

---

## Summary

| Check | Result |
|-------|--------|
| @rendermode guard (WI908) | ✅ PASS |
| IMudDialogInstance | ✅ CLEAN |
| Aurora ALTER try/catch 1060 | ✅ PASS |
| CREATE TABLE IF NOT EXISTS | ✅ PASS |
| DESIGN-SYSTEM (Variant/Icons/Style) | ✅ PASS |
| Single-primary contact constraint | ✅ PRESENT |
| AssignOwnerAsync wired | ✅ PASS |
| AddNoteAsync wired | ✅ PASS |
| PortalName = TIG Dashboard | ✅ PASS |
| Commit scope (famos/ only) | ✅ PASS |
| AWSSDK.S3 packages | ✅ EXPECTED |
| OpportunityWorkspace panels wired | ✅ PASS |

**Critical issues:** 0
**Important issues:** 0
**Nitpicks:** 2 (bucket name config, upload size label)

**VERDICT: PASS — advance to DEPLOY.**
