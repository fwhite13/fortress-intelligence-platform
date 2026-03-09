# Review Report: FAIT-KB-REDESIGN-PHASE2A

### Verdict: NEEDS-CHANGES

**Reviewer:** Hawkeye  
**Commit:** `f60ee93`  
**Review Cycle:** 1 of 2  
**Date:** 2026-03-09

---

## Consistency Audit

**Files Cross-Referenced:**

| Check | Result |
|---|---|
| `KbTeam.cs` ↔ `AppDbContext.cs` (KbTeams DbSet, `kb_teams` table) | ✅ |
| `KbTeamMember.cs` ↔ `AppDbContext.cs` (KbTeamMembers DbSet, `kb_team_members` table) | ✅ |
| `KbEntry.cs` `TeamId`/`KbTier.Team` ↔ `AppDbContext.cs` FK config | ✅ |
| `ConversationTeamKb.cs` `KbTeam?` navigation ↔ `AppDbContext.cs` | ✅ |
| `ForgeService.cs` method names ↔ `ChatView.razor` / `KnowledgeBaseManagement.razor` callers | ✅ |
| `KbTier.Team = 1` ↔ prior `KbTier.Project = 1` | ✅ |
| `AppDbContextModelSnapshot.cs` ↔ `KbTeam`, `KbTeamMember`, `KbEntry` | ✅ |
| **`AppDbContextModelSnapshot.cs` `Conversation` entity `ProjectId` → ❌ renamed to `"TeamId"`** | ❌ **CRITICAL** |
| DO NOT TOUCH: `ProjectDetail.razor`, `DocumentUpload.razor`, `DocumentService.cs`, `ProjectService.cs` | ✅ untouched |
| DO NOT TOUCH: `conversation.ProjectId` (model) | ✅ untouched |
| DO NOT TOUCH: S3 path `kb-docs/project/` in `UploadProjectDocumentAsync` | ✅ untouched |
| `KbDocumentService.cs` KbTier references | ✅ — `KbTier.Team` in all 3 locations |
| `DatabaseInitializationService.cs` migration table targets | ✅ — `conversation_team_kbs` NOT in rename list |
| `kb_entries.team_id` CHANGE COLUMN nullability | ✅ — `INT` (nullable) preserved |
| `kb_team_members.team_id` CHANGE COLUMN not-null preserved | ✅ — `INT NOT NULL` preserved |

**Undocumented Dependencies Found:**
- `AdminIndex.razor` — calls `Forge.GetCorporateEntriesAsync()`, `Forge.CreateEntryAsync()`, `Forge.UpdateEntryAsync()`, `Forge.DeleteEntryAsync()`. All are non-team methods. ✅ Unaffected by rename.

---

## Critical Issues — 1

### C1: ModelSnapshot incorrectly renames `Conversation.ProjectId` → `"TeamId"` 
- **File:** `src/FortressAI.Web/Migrations/AppDbContextModelSnapshot.cs` (lines 229–249)
- **Category:** consistency — DO NOT TOUCH violation in snapshot
- **Issue:** The `Conversation` entity in the snapshot now has a `Guid?` property named `"TeamId"` with a `HasIndex("TeamId")`, and the FK relationship for `Conversation → Project` uses `HasForeignKey("TeamId")`. In every prior migration Designer.cs, this property was correctly named `"ProjectId"`. The actual `Conversation` C# model still has `ProjectId` (correct, untouched). The snapshot is now **inconsistent with the live model and with AppDbContext** which configures this FK via `HasForeignKey(e => e.ProjectId)`.

**Evidence (snapshot, lines 229–249 + 1115–1122):**
```csharp
// Snapshot — WRONG
b.Property<Guid?>("TeamId")     // ← should be "ProjectId"
    .HasColumnType("char(36)");
b.HasIndex("TeamId");           // ← should be "ProjectId"

modelBuilder.Entity("FortressAI.Shared.Models.Conversation", b =>
{
    b.HasOne("FortressAI.Shared.Models.Project", "Project")
        .WithMany("Conversations")
        .HasForeignKey("TeamId")   // ← should be "ProjectId"
        .OnDelete(DeleteBehavior.SetNull);
});
```

**Evidence (prior Designer.cs — correct):**
```csharp
// 20260305204521_AddConversationTeamKbs.Designer.cs — CORRECT
b.Property<Guid?>("ProjectId")
    .HasColumnType("char(36)");
b.HasIndex("ProjectId");
// FK: .HasForeignKey("ProjectId")
```

- **Impact:** EF Core uses the snapshot to generate future migrations. Any new `dotnet ef migrations add` will diff against a model where `Conversation.TeamId` is the FK to `Project`. The next migration will therefore try to rename the `ProjectId` column to `team_id` in the `conversations` table — corrupting the actual Project FK in the database. This is a silent time-bomb that will only fire on the next migration, not at runtime.

- **Fix:** In `AppDbContextModelSnapshot.cs`, revert the `Conversation` entity to use `"ProjectId"` everywhere:
```diff
- b.Property<Guid?>("TeamId")
+ b.Property<Guid?>("ProjectId")
      .HasColumnType("char(36)");

- b.HasIndex("TeamId");
+ b.HasIndex("ProjectId");

  // In the FK section:
  b.HasOne("FortressAI.Shared.Models.Project", "Project")
      .WithMany("Conversations")
-     .HasForeignKey("TeamId")
+     .HasForeignKey("ProjectId")
      .OnDelete(DeleteBehavior.SetNull);
```

---

## Important Issues — 0

_None found._

---

## Nitpicks — 4

### N1: Stale `projectId` parameter name in `SaveEntry`
- **File:** `KnowledgeBaseManagement.razor` (line 571)
- `private async Task SaveEntry(KbTier tier, int? projectId, ...)` — parameter is named `projectId` but semantically is now `teamId`. It's passed correctly to `CreateEntryAsync` as `teamId`, so no functional impact. Not blocking.

### N2: Stale UI text — "No entries in this project yet"
- **File:** `KnowledgeBaseManagement.razor` (line 315)
- The empty-state message reads "No entries in this project yet. Add the first one." — should read "No entries in this team yet." Not blocking (UI text only, zero functional impact), but inconsistent with the rename intent.

### N3: Stale snackbar strings — "Project created!" / "Failed to create project"
- **File:** `KnowledgeBaseManagement.razor` (lines 760, 767)
- `CreateTeam()` emits `"Project created!"` and `"Failed to create project: ..."` Functionally correct, cosmetically stale. Should be "Team created!" / "Failed to create team:".

### N4: Stale local variable name `projectsTask`
- **File:** `KnowledgeBaseManagement.razor` (line 471)
- `var projectsTask = Forge.GetUserTeamsAsync(...)` — variable still named `projectsTask` instead of `teamsTask`. No functional impact.

---

## Acceptance Criteria Verification

| Criterion | Status | Notes |
|---|---|---|
| 1. Completeness — all `KbProject`/`KbProjectMember`/`KbProjectRole`/`KbTier.Project` gone from active source | ✅ PASS | `grep` returned zero results across all .cs and .razor files (excl. Designer.cs history) |
| 2. DO NOT TOUCH compliance | ⚠️ PARTIAL | `ProjectDetail.razor`, `DocumentService.cs`, `DocumentUpload.razor`, `ProjectService.cs`, `conversation.ProjectId` model, S3 `kb-docs/project/` path — all ✅ untouched. **But** `AppDbContextModelSnapshot.cs` incorrectly renamed `Conversation.ProjectId` shadow property → `"TeamId"` (see C1). |
| 3. `KbTier.Team` integer value = 1 | ✅ PASS | `KbEntry.cs`: `public enum KbTier { Personal = 0, Team = 1, Corporate = 2 }` |
| 4a. `conversation_team_kbs.team_id` NOT renamed | ✅ PASS | Not in the `renameSqls` array; only `kb_projects` and `kb_project_members` are renamed |
| 4b. `kb_entries.team_id` nullability | ✅ PASS | `CHANGE COLUMN project_id team_id INT` (no NOT NULL) → nullable preserved. DDL `CREATE TABLE IF NOT EXISTS` also uses `TeamId INT NULL` |
| 4c. `kb_team_members.team_id` FK to `kb_teams.id` | ✅ PASS | `AppDbContext`: `e.HasMany(x => x.Members).WithOne(m => m.Team).HasForeignKey(m => m.TeamId)` — FK preserved. CHANGE COLUMN uses `INT NOT NULL` matching original. |
| 5. AppDbContext FKs use new type names | ✅ PASS | `KbTeamMember.TeamId → KbTeam.Id`, `ConversationTeamKb.TeamId → KbTeam.Id`, `KbEntry.TeamId → KbTeam.Id` all correct |
| 6. ForgeService public API methods | ✅ PASS | `GetUserTeamsAsync`, `CreateTeamAsync`, `GetTeamEntriesAsync`, `IsTeamMemberAsync` all present. No callers using old names found. |
| 7. KnowledgeBaseManagement.razor markup | ✅ PASS (with nitpicks) | No `@project` iteration vars, no `@entry.ProjectId`. Code-behind fully renamed. UI strings have minor stale text (N1–N4) |
| 8. Migration idempotency | ✅ PASS | Error codes 1050/1146 (table rename), 1054/1060/1091 (column rename) cover all idempotency scenarios for both fresh and migrated DBs |
| 9. ModelSnapshot coherence | ❌ FAIL | `Conversation` entity shadow property renamed incorrectly — `"ProjectId"` → `"TeamId"` (see C1) |
| 10. Scope creep — no logic changes | ✅ PASS | Pure rename throughout. No new methods, no changed behavior, no removed error handling. |

---

## Positive Observations

- **Migration architecture is solid.** The two-phase approach (try-catch RENAME TABLE + try-catch CHANGE COLUMN, each with appropriate MySQL error codes, guarded by `applied_migrations`) is exactly right for an always-on service doing schema changes safely.
- **KbTier integer value discipline.** `Team = 1` explicitly set — the engineer correctly understood the data migration risk and guarded against it.
- **`conversation_team_kbs` protection.** The rename SQL list correctly excludes `conversation_team_kbs`, which already uses `team_id`. This was a real risk and it was handled correctly.
- **DO NOT TOUCH compliance was nearly perfect.** All actual Project entities — models, services, razors, DB tables, and S3 paths — were untouched. The snapshot issue is the only violation.
- **ForgeService is clean.** Public API names are consistent, all callers updated, no orphaned references.
- **KbTier.Team = 1 confirmed.** Numeric enum value correctly preserved — no data migration needed.

---

## Summary

This is a very clean rename PR. One fix required before merge:

**The ModelSnapshot incorrectly renamed `Conversation.ProjectId` to `"TeamId"`.** This is a DO NOT TOUCH violation that will corrupt the next EF migration's diff if not fixed. It doesn't break the running app today (the snapshot isn't used at runtime), but it's a ticking migration time bomb. The fix is a targeted 3-line edit to the snapshot — straightforward.

The 4 nitpicks (stale variable/string names in `KnowledgeBaseManagement.razor`) are cosmetic and do not block. Recommend fixing in this PR since it's a rename PR, but your call.

**Required fix before PASS:** C1 only.

---
_Review by Hawkeye — 2026-03-09_

---

## Review Cycle 2: Fix Verification

**Reviewer:** Hawkeye  
**Commit:** `75e9850`  
**Review Cycle:** 2 of 2  
**Date:** 2026-03-09

### Verdict: ✅ PASS

All four items from the Cycle 1 NEEDS-CHANGES verdict have been correctly addressed. No regressions introduced.

---

### C1 Verification — `AppDbContextModelSnapshot.cs`

**All three occurrences in the `Conversation` entity block correctly reverted to `"ProjectId"`:**

| Location | Expected | Actual | Status |
|---|---|---|---|
| `b.Property<Guid?>(...)` (line 229) | `"ProjectId"` | `"ProjectId"` | ✅ |
| `b.HasIndex(...)` (line 246) | `"ProjectId"` | `"ProjectId"` | ✅ |
| `HasForeignKey(...)` in FK section (line 1119) | `"ProjectId"` | `"ProjectId"` | ✅ |

**Non-Conversation `"TeamId"` references verified untouched and correct:**

| Entity | Property/Index/FK | Status |
|---|---|---|
| `ConversationTeamKb` | `"TeamId"` (property, composite key, index, FK) | ✅ correct — these are KB-join-table TeamId refs |
| `KbEntry` | `"TeamId"` (property, index, FK) | ✅ correct |
| `KbTeamMember` | `"TeamId"` (property, composite index, FK) | ✅ correct |
| `ProjectDocument` | `"TeamId"` (property, index, FK) | ✅ correct — pre-existing from `f60ee93`, not introduced by this fix |

**No `"ProjectId"` references accidentally introduced in non-Conversation entity blocks.** The entity-to-field mapping is exactly correct across the full snapshot.

---

### N1–N4 Verification — `KnowledgeBaseManagement.razor`

| Nitpick | Fix Applied | Status |
|---|---|---|
| N1: `SaveEntry` parameter `projectId` → `teamId` | `private async Task SaveEntry(KbTier tier, int? teamId, ...)` — parameter renamed and usage updated (`teamId` passed to `CreateEntryAsync`) | ✅ |
| N2: Empty-state text | `"No entries in this team yet. Add the first one."` | ✅ |
| N3: Snackbar strings | `"Team created!"` / `"Failed to create team: {ex.Message}"` | ✅ |
| N4: `projectsTask` → `teamsTask` | All 3 usages updated (`var teamsTask`, `Task.WhenAll(..., teamsTask)`, `_teams = teamsTask.Result`) | ✅ |

Zero regressions found. No stale `projectId`/`projectsTask`/`project` strings remain in the file.

---

### Build Verification

```
dotnet build src/FortressAI.Web/FortressAI.Web.csproj
  27 Warning(s)    ← pre-existing MUD0002 warnings, none new
  0 Error(s)
```

Build is clean. No new warnings introduced by either fix.

---

### Summary

The engineer correctly applied all required fixes — exact targets, no over-reach, no regressions. The snapshot `Conversation` entity is now coherent with the live C# model and `AppDbContext`. The razor file is fully renamed with no stale references remaining. Build continues to pass.

**This PR is cleared for Stage 4 (Security Scan).**

---
_Review Cycle 2 by Hawkeye — 2026-03-09_
