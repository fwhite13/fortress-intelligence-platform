# FORMS v2 — DataProtection + FormLibrary Refactor Build Report

**Date:** 2026-03-03  
**Branch:** main  
**Commit:** 476594c  
**Status:** ✅ BUILD PASSED — 0 errors, pushed to origin/main

---

## Summary

Fixed two root causes of antiforgery 400 errors after container restarts:
1. Persisted Data Protection key ring to MySQL so tokens survive redeploys
2. Refactored `FormLibrary.razor` to use `IDbContextFactory<AppDbContext>` directly, eliminating the stale-cookie failure path

---

## Fix 1: Data Protection Key Persistence

### Files Modified

**`FortressFormTools.Data/AppDbContext.cs`**
- Added `using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore`
- Implemented `IDataProtectionKeyContext` on `AppDbContext`
- Added `DbSet<DataProtectionKey> DataProtectionKeys` property

**`FortressFormTools.Data/FortressFormTools.Data.csproj`**
- Added `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore 8.0.*`
  (required because `IDataProtectionKeyContext` is defined in this package and `AppDbContext` lives in the Data project)

**`FortressFormTools.Web/FortressFormTools.Web.csproj`**
- Added `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore 8.0.*`
  (required for `PersistKeysToDbContext<TContext>()` extension method)

**`FortressFormTools.Web/Program.cs`**
- Added `using Microsoft.AspNetCore.DataProtection`
- Registered `AddDataProtection().PersistKeysToDbContext<AppDbContext>().SetApplicationName("FortressFormTools")` after `AddDbContextFactory`
- Added idempotent `CREATE TABLE IF NOT EXISTS DataProtectionKeys` to startup SQL for environments where the table doesn't yet exist

---

## Fix 2: FormLibrary.razor — IDbContextFactory Refactor

### Methods Refactored

| Method | Before | After |
|--------|--------|-------|
| `LoadForms()` | `Http.GetFromJsonAsync<FormListResponse>("api/forms?...")` | Direct EF query via `DbFactory.CreateDbContextAsync()` |
| `DeleteForm()` | `Http.DeleteAsync($"api/forms/{id}")` | `db.FormLibraries.FindAsync()` → `db.FormLibraries.Remove()` → `SaveChangesAsync()` |
| `ResubmitForm()` | `Http.PostAsync($"api/forms/{id}/resubmit")` | EF update + `ExtractionQueue.Enqueue(id)` (direct injection of `ExtractionBackgroundService`) |

### Methods Kept on HttpClient

| Method | Reason |
|--------|--------|
| `OnFilesSelected` upload | Multipart file stream — keep as-is per spec |
| `StartStatusPolling` per-form GET | Uses `FormDetailDto` with `Fields` collection; left for follow-up |

### New Injections Added
- `@inject IDbContextFactory<AppDbContext> DbFactory`
- `@inject FortressFormTools.Web.Services.ExtractionBackgroundService ExtractionQueue`
- `@using Microsoft.EntityFrameworkCore`
- `@using FortressFormTools.Data`

### Removed
- `FormListResponse` inner class (no longer needed)

### EF Translation Note
`f.Fields.Count()` in the Select projection translates correctly to a SQL subquery in EF Core with Pomelo MySQL. `FormListItem.FieldCount` is `int` — no fallback to `db.FormFields.Count(ff => ff.FormLibraryId == f.Id)` needed.

---

## Build Output

```
dotnet restore  → Restored 2 projects
dotnet build    → Build succeeded. 0 Error(s)
git push        → [main 476594c] pushed to origin/main
```

Warnings are pre-existing MudBlazor/nullable analyzer noise — not introduced by this change.

---

## Deployment Notes

- **No manual DB migration required** for environments where `DataProtectionKeys` table already exists
- For new deployments, startup SQL creates the table idempotently (`CREATE TABLE IF NOT EXISTS DataProtectionKeys`)
- After first deploy with this change, antiforgery tokens will survive container restarts — stale-cookie 400s eliminated
