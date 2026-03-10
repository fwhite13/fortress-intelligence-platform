# FAIT Processing Chip Fix — Round 2 Build Report

**Date:** 2026-03-10  
**Branch:** main  
**Commit:** e33c3d4  
**Build result:** ✅ 0 errors, 28 warnings (all pre-existing, none introduced)

---

## Problem Summary

`UploadDocumentAsync` (personal/team/corp KB uploads) never created a `ProjectDocuments` DB row — only S3 upload + metadata. The processing chip fix in `ListDocumentsAsync` looks up `ProjectDocuments.IngestionStatus` by S3Key, finds nothing, and defaults to `"pending"` forever.

Root cause: `ProjectDocument.ProjectId` was `NOT NULL`, making it impossible to create tracking rows for personal/team KB uploads (which have no project).

---

## Fix Approach

Made `ProjectDocument.ProjectId` nullable (`Guid?`). Personal/team/corp KB uploads now create a `ProjectDocuments` row with `ProjectId = null`. The existing polling loop in `KbSyncRetryService` uses time-based `WHERE IngestionStatus = 'pending' AND UploadedAt <= syncStartedAt` — catches all rows regardless of `ProjectId`.

---

## Files Changed

| File | Change |
|------|--------|
| `src/FortressAI.Shared/Models/ProjectDocument.cs` | `ProjectId` changed from `Guid` to `Guid?` with comment |
| `src/FortressAI.Web/Data/AppDbContext.cs` | Added `.IsRequired(false)` to FK relationship for `ProjectDocument.ProjectId` |
| `src/FortressAI.Web/Services/KbDocumentService.cs` | Added DB tracking row insert in `UploadDocumentAsync` after metadata upload |
| `src/FortressAI.Web/Services/DatabaseInitializationService.cs` | Added `kb-documents-nullable-projectid-v1` migration block |
| `src/FortressAI.Web/Migrations/AppDbContextModelSnapshot.cs` | Updated `ProjectDocument` entity: `TeamId` → `ProjectId` (nullable `Guid?`), index, FK relationship without `.IsRequired()` |
| `src/FortressAI.Web/Services/DocumentService.cs` | Added `.Value` at two call sites where nullable `doc.ProjectId` is passed to `UploadProjectDocumentAsync(Guid projectId, ...)` |

---

## DocumentService.cs — Project KB Rows Already Created (No Duplicate Insert)

**Confirmed:** `DocumentService.cs` already creates `ProjectDocuments` rows for project KB uploads:

```csharp
// Line ~51-62 in UploadDocumentAsync:
var doc = new ProjectDocument
{
    Id = Guid.NewGuid(),
    ProjectId = projectId,
    Filename = filename,
    ...
    IngestionStatus = "none"
};
db.ProjectDocuments.Add(doc);
```

The row is created with `IngestionStatus = "none"` initially, then updated to `"pending"` after S3 upload succeeds (line ~81). `KbDocumentService.UploadProjectDocumentAsync` does NOT add a duplicate row — it only handles S3 upload + metadata. Therefore **no duplicate insert was added** to `UploadProjectDocumentAsync`.

---

## DB Migration

**Migration name:** `kb-documents-nullable-projectid-v1`

**SQL executed (once, guarded by `applied_migrations` table):**
```sql
ALTER TABLE project_documents MODIFY COLUMN ProjectId char(36) NULL
```

This is safe: the FK constraint is still enforced when `ProjectId` is non-null; rows with `ProjectId = null` are personal/team/corp KB uploads.

**Placement:** Added after `kb-team-rename-v1` block in `DatabaseInitializationService.StartAsync`.

---

## Build Verification

```
dotnet build src/FortressAI.Web/FortressAI.Web.csproj
  28 Warning(s)
  0 Error(s)
```

All 28 warnings are pre-existing (MudBlazor analyzer warnings, nullable reference warnings in existing code). Zero warnings introduced by this change.

---

## Model + Config Verification

```
grep -n "ProjectId" src/FortressAI.Shared/Models/ProjectDocument.cs
6:    public Guid? ProjectId { get; set; }  // null for personal/team/corp KB uploads

grep -n "IsRequired" src/FortressAI.Web/Data/AppDbContext.cs | head -5
78: entity.HasOne(e => e.Project)...HasForeignKey(e => e.ProjectId).IsRequired(false).OnDelete(...)
```
