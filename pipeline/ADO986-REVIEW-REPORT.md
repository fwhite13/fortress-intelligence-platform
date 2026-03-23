# Review Report — ADO #986: Team Notes Page

**Reviewer:** Hawkeye (Clint Barton) — code-reviewer  
**Commit:** `21d6a6f`  
**Review Cycle:** 1  
**Date:** 2026-03-21  
**Verdict:** ✅ PASS

---

## Checklist Results

### Entity + DbContext

| # | Item | Verdict | Notes |
|---|------|---------|-------|
| 1 | `TeamNote` has all 6 fields (Id, AuthorId, NoteText, OpportunityId, TeamTag, CreatedAt) | ✅ PASS | All present in `TeamNote.cs` |
| 2 | `DbSet<TeamNote> TeamNotes` in DbContext | ✅ PASS | `FamOsDbContext.cs` |
| 3 | `OnModelCreating` snake_case `HasColumnName()` for ALL properties | ✅ PASS | All 6 properties mapped: `id`, `author_id`, `note_text`, `opportunity_id`, `team_tag`, `created_at` |
| 4 | No EF migration files — raw SQL in startup | ✅ PASS | Raw SQL only; no migration files in diff |

### Program.cs

| # | Item | Verdict | Notes |
|---|------|---------|-------|
| 5 | `CREATE TABLE IF NOT EXISTS team_notes` SQL block present | ✅ PASS | Present in Program.cs startup block |
| 6 | `opportunity_id` uses `CHAR(36) CHARACTER SET ascii COLLATE ascii_general_ci NULL` | ✅ PASS | Exact match in DDL |
| 7 | `AddScoped<TeamNoteService>()` registered | ✅ PASS | Confirmed in DI registration block |

### TeamNoteService.cs

| # | Item | Verdict | Notes |
|---|------|---------|-------|
| 8 | `GetNotesAsync` — date arithmetic done AFTER `.ToListAsync()` | ✅ PASS | No date arithmetic inside EF query at all; concern does not apply |
| 9 | `GetAccountsForDropdownAsync` — non-closed opps, ordered by name | ✅ PASS | `.Where(o => !o.IsClosed).OrderBy(o => o.Name)` |
| 10 | `PostNoteAsync` uses `IDbContextFactory` pattern | ✅ PASS | Constructor: `IDbContextFactory<FamOsDbContext> dbFactory`; all methods call `dbFactory.CreateDbContextAsync()` |

### Notes.razor

| # | Item | Verdict | Notes |
|---|------|---------|-------|
| 11 | `@page "/notes"` + `@attribute [Authorize]` | ✅ PASS | Lines 1–2 of Notes.razor |
| 12 | Compose form: MudTextField, account dropdown, Post button | ✅ PASS | All three present |
| 13 | Filter dropdown uses `Value` + `ValueChanged` (NOT `@bind-Value`) | ✅ PASS | `Value="_filterOppId" ValueChanged="OnFilterChanged"` — correct pattern |
| 14 | Notes list: avatar initials, team tag badge, timestamp, note text | ✅ PASS | All four elements in the `@foreach` render block |
| 15 | Empty state uses `.famos-empty-state` CSS class | ✅ PASS | `<div class="famos-empty-state">` |
| 16 | `PostNoteAsync` has try/catch + `finally { _posting = false; }` | ✅ PASS | Correct structure |
| 17 | No `@onclick="() => AsyncMethod()"` without async/await | ✅ PASS | Uses `OnClick="PostNoteAsync"` — Blazor `EventCallback` awaits correctly |

### NavMenu + CSS

| # | Item | Verdict | Notes |
|---|------|---------|-------|
| 18 | Team Notes nav item added between Accounts and Reports | ✅ PASS | Inserted after Accounts `NavLink`, before disabled Reports `<span>` |
| 19 | `.famos-note-team-tag`, `.famos-tag-tig`, `.famos-tag-higg` CSS classes present | ✅ PASS | All three defined at end of famos.css |
| 20 | No duplicate `.famos-empty-state` definition | ✅ PASS | Pre-existing at line 698; not redefined in this commit |

---

## Issues Found

### Important (non-blocking)

**I1 — Silent exception swallow in `PostNoteAsync`**  
File: `Notes.razor`, `@code` block  
```csharp
catch (Exception ex)
{
    // Non-fatal — could add snackbar later
}
```
The catch discards `ex` entirely. If `SaveChangesAsync` fails (DB outage, constraint violation), the user sees the form reset with no feedback and no log entry. The comment acknowledges it, but at minimum `Logger.LogError(ex, "PostNoteAsync failed")` should be added before the snackbar lands. Not blocking ship, but a follow-up task.

**I2 — `TeamTag` hardcoded to `"TIG"` in `PostNoteAsync`**  
File: `Services/TeamNoteService.cs`  
```csharp
TeamTag = "TIG",
```
The entity, CSS, and render logic all support `"Higg"`, but there's no way to post a Higg note today. Confirm whether TIG-only is the intended scope for this WI, or if a team tag selector in the compose form was missed. If TIG-only is intentional, a `// TIG-only for now` comment would clarify.

### Nitpick

**N1 — `_userId` set once on init**  
In a long-lived Blazor Server circuit, if the auth session rotates, `_userId` won't refresh until the circuit is recreated. Consistent with other pages in this codebase, so not a new risk — just flagging the pattern.

**N2 — Linear account lookup in render loop**  
`_accounts.FirstOrDefault(a => a.Id == note.OpportunityId)` is called per-note. With `Take(50)` and a modest accounts list, it's negligible, but a `Dictionary<Guid, string>` would be cleaner at scale.

---

## Summary

Clean, focused commit. 7 files, 250 insertions, exactly scoped to the feature. No checklist failures. The two Important items are non-blocking — I1 is a known gap with a comment, I2 needs a quick product clarification. Code quality is consistent with the rest of the codebase.

**Verdict: ✅ PASS — clear to advance to DEPLOY.**

---

*Hawkeye out. — Clint Barton*
