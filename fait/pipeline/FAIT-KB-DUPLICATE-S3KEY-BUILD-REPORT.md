# Build Report: FAIT-KB-DUPLICATE-S3KEY

**Date:** 2026-03-12  
**Agent:** Tony Stark (software-engineer)  
**Commit:** `43a30f4`  
**Branch:** main  
**Build result:** ✅ `Build succeeded. 0 Error(s), 31 Warning(s)` (pre-existing MUD0002 analyzer warnings — not introduced by this change)

---

## Bug Summary

When a user deletes a KB document and re-uploads a file with the same filename, `ListDocumentsAsync` built a Dictionary keyed by S3 path. `DeleteDocumentAsync` only deleted from S3 — it did **not** delete the `project_documents` DB row — leaving a stale row. When the file was re-uploaded, a new row was inserted. Two rows with the same `S3Key` then caused `ToDictionaryAsync` to throw `ArgumentException: An item with the same key has already been added`, propagating up and rendering the KB page blank.

---

## Fix 1: Deduplicate in ListDocumentsAsync

**File:** `src/FortressAI.Web/Services/KbDocumentService.cs`  
**Original line:** ~382 (was the `ToDictionaryAsync` call)

**Original code (the duplicate-key source):**
```csharp
var statusMap = await db.ProjectDocuments
    .Where(pd => pd.S3Key != null && s3Keys.Contains(pd.S3Key))
    .ToDictionaryAsync(pd => pd.S3Key!, pd => pd.IngestionStatus);
```

**Replaced with GroupBy dedup:**
```csharp
var dbRows = await db.ProjectDocuments
    .Where(pd => pd.S3Key != null && s3Keys.Contains(pd.S3Key))
    .ToListAsync();

var grouped = dbRows.GroupBy(r => r.S3Key!).ToList();
var duplicates = grouped.Where(g => g.Count() > 1).ToList();
if (duplicates.Any())
{
    foreach (var dup in duplicates)
        _logger.LogWarning("[KbDocumentService] Duplicate S3Key detected: {S3Key} ({Count} rows) — keeping most recent to avoid Dictionary collision", dup.Key, dup.Count());
}

var statusMap = grouped
    .Select(g => g.OrderByDescending(r => r.UploadedAt).First())
    .ToDictionary(r => r.S3Key!, r => r.IngestionStatus);
```

This is a **resilience fix** — prevents the throw entirely by deduplicating before dictionary construction, and logs a warning when duplicates are detected so the issue is observable in logs.

---

## Fix 2: DeleteDocumentAsync — Now Hard-Deletes DB Row

**File:** `src/FortressAI.Web/Services/KbDocumentService.cs`  
**Method:** `DeleteDocumentAsync`

**Delete type before fix:** **S3-only** — deleted from S3 bucket (both the object and `.metadata.json` companion) but left the `project_documents` DB row in place. This was the root cause — the stale DB row persisted through delete.

**After fix:** Hard-deletes all matching `project_documents` rows for the S3Key after the S3 deletion. Uses `RemoveRange` to catch any pre-existing duplicates in a single pass. Wrapped in try/catch (non-fatal — S3 delete already succeeded).

```csharp
// Remove DB tracking row(s) — prevents duplicate-S3Key bug on re-upload
try
{
    await using var db = await _dbContextFactory.CreateDbContextAsync();
    var rows = await db.ProjectDocuments
        .Where(pd => pd.S3Key == s3Key)
        .ToListAsync();
    if (rows.Any())
    {
        db.ProjectDocuments.RemoveRange(rows);
        await db.SaveChangesAsync();
        _logger.LogInformation("[KbDocumentService] Removed {Count} DB tracking row(s) for S3Key={S3Key}", rows.Count, s3Key);
    }
}
catch (Exception ex)
{
    _logger.LogWarning(ex, "[KbDocumentService] Failed to remove DB tracking row for S3Key={S3Key} — non-fatal, stale row may remain", s3Key);
}
```

---

## Fix 3: UploadDocumentAsync — Upsert Pattern

**File:** `src/FortressAI.Web/Services/KbDocumentService.cs`  
**Method:** `UploadDocumentAsync`

Changed the DB tracking block from a blind `INSERT` to an upsert: if a row exists for the S3Key, it updates the existing row (resets `IngestionStatus = "pending"`, `UploadedAt = now`). Only inserts a new row if no existing row is found. This is the data-integrity layer on top of the delete fix.

---

## Fix 4: DB Schema — Dedup SQL + Unique Constraint

**File:** `src/FortressAI.Web/Services/DatabaseInitializationService.cs`  
**Location:** After the `alterStatements` foreach loop, before the "Seed Brave Search MCP server" block (~line 390)

**Dedup SQL (runs on every startup, idempotent):**
```sql
DELETE p1 FROM project_documents p1
INNER JOIN project_documents p2
WHERE p1.S3Key = p2.S3Key
AND p1.UploadedAt < p2.UploadedAt
AND p1.Id != p2.Id
```
Keeps the most-recent row per S3Key. Removes all older duplicates. Wrapped in try/catch (non-fatal).

**Unique constraint:**
```sql
ALTER TABLE project_documents
ADD CONSTRAINT uq_project_documents_s3key UNIQUE (S3Key)
```
Idempotent via `catch (MySqlException ex) when (ex.Number == 1061)`. The dedup step runs first to clear existing duplicates so the constraint add won't fail on dirty data.

**Unique constraint added:** ✅ Yes — will be applied on next app startup against the live DB.

---

## Fix 5: KnowledgeBaseManagement.razor — Error State

**File:** `src/FortressAI.Web/Components/Pages/KnowledgeBaseManagement.razor`

Added `_personalDocumentsLoadError` string field. In `OnInitializedAsync`, the existing inner try-catch now also sets `_personalDocumentsLoadError` when document loading fails. On success, it clears to `null`.

In the Razor template, the document list section now checks `_personalDocumentsLoadError` first:
- If set: renders a `<MudAlert Severity="Severity.Error">` with the error message
- If null and documents exist: renders the document list (existing behavior)

This ensures a blank page is never shown — users see a clear "Failed to load your documents. Please refresh the page." message.

---

## Self-Review Checklist

- [x] All acceptance criteria from task brief addressed
- [x] Build: 0 errors
- [x] No new warnings introduced (31 pre-existing MUD0002 warnings remain)
- [x] Delete method now hard-deletes DB row (root cause of duplicate)
- [x] Upload uses upsert pattern (prevents future duplicates)
- [x] ListDocumentsAsync deduplicates gracefully (resilience — handles any pre-existing stale rows)
- [x] DB startup migration: dedup SQL + unique constraint (enforcement layer)
- [x] Razor error state: users see error message instead of blank page
- [x] All DB operations wrapped in try/catch (non-fatal where appropriate)
- [x] Committed and pushed: `43a30f4`

---

## Files Modified

| File | Changes |
|------|---------|
| `src/FortressAI.Web/Services/KbDocumentService.cs` | ListDocumentsAsync dedup, DeleteDocumentAsync DB row removal, UploadDocumentAsync upsert |
| `src/FortressAI.Web/Services/DatabaseInitializationService.cs` | Dedup SQL migration + unique constraint on S3Key |
| `src/FortressAI.Web/Components/Pages/KnowledgeBaseManagement.razor` | `_personalDocumentsLoadError` field + error state in template |
