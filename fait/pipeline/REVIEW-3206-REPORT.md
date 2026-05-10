# Review Report — ADO#3206

### Verdict: NEEDS-CHANGES

---

## CC Review Summary

CC invoked with adversarial brief covering all 8 critical + 4 important + 4 nitpick checks. CC read all 10 files and produced a structured finding report. One real failure identified (I1: missing FK constraints in migration). Two additional findings surfaced beyond the brief checklist (A1: S3-before-DB orphan risk in SaveUploadAsync; A3: filename not sanitized in S3 key). All 8 critical checks passed. No false positives.

**CC invocation:**
```bash
cd /home/fredw/projects/fip/fait && cat /tmp/clint-review-brief-3206.md | claude --model sonnet --print --dangerously-skip-permissions
```

---

## Spec Compliance Check

Not a spec-reference task (no `**Spec reference:**` field). Review is against ADO#3206 acceptance criteria from the build report.

**Acceptance Criteria:**
- ✅ WorkspaceFolder + WorkspaceUpload EF models created
- ✅ DbSets added to AppDbContext
- ✅ Migration `AddWorkspaceUploads` created and applied to fait_dev
- ✅ IWorkspaceUploadService + WorkspaceUploadService implemented
- ✅ Registered Scoped in Program.cs
- ✅ WorkspaceController: folder CRUD + file upload + file delete + presigned download
- ✅ 50MB upload limit enforced (server-side check returning 413)
- ✅ WorkspaceFiles.razor Files tab complete (toolbar, breadcrumb, folder/file lists, empty state)
- ✅ CSS variables only (0 hardcoded hex)
- ✅ Harness list_files + read_file in BUILTIN_TOOLS
- ❌ Migration FK constraints missing (migration applied, but without the FK DDL specified)

**Spec compliance verdict:** ❌ NON-COMPLIANT on FK constraints — blocks PASS

---

## Consistency Audit

**Files Cross-Referenced:**
- `WorkspaceFolder.cs` ↔ `AppDbContext.cs` OnModelCreating — ✅ column name mappings match entity properties
- `WorkspaceUpload.cs` ↔ `AppDbContext.cs` OnModelCreating — ✅ column name mappings match entity properties
- `AppDbContext.cs` entity config ↔ `20260510194616_AddWorkspaceUploads.cs` — ✅ column types and names match; ❌ FK constraints absent from both (entity config has no `HasOne`/`WithMany`, migration has no `AddForeignKey`)
- `WorkspaceUploadService.cs` S3 key construction ↔ `list_files` harness handler expected path pattern — ✅ consistent
- harness `userId` at `/turn` dispatch ↔ `list_files`/`read_file`/`read_memory`/`write_memory`/`create_document` cases — ✅ all use `userId` (line 1294 definition)
- `BUILTIN_TOOLS` Set ↔ dispatch cases — ✅ `list_files` and `read_file` present in Set AND in dispatch

**Undocumented Dependencies Found:**
- `WorkspaceController.cs` uses `IDbContextFactory<AppDbContext>` (injected separately from `IWorkspaceUploadService`) — ✅ consistent with codebase pattern

---

## Issues Found

| Severity | File | Line(s) | Issue | Fix |
|----------|------|---------|-------|-----|
| Important | `AppDbContext.cs` / migration | 523–550 | FK constraints missing — no `HasOne`/`WithMany`/`OnDelete` for WorkspaceFolder self-ref or WorkspaceUpload→folder relationship | Add FK config to `AppDbContext.cs` OnModelCreating (see C1 detail below), generate new migration |
| Important | `WorkspaceUploadService.cs` | 112, 135 | S3 `PutObjectAsync` fires before `db.SaveChangesAsync()` — if DB write fails, S3 object is orphaned with no DB record to track it | Swap order: save DB row first, then upload to S3; OR catch DB failure and call `DeleteObjectAsync` to rollback |
| Nitpick | `WorkspaceController.cs` | ~161 | `file.FileName` passed directly into S3 key construction without `Path.GetFileName()` sanitization — filenames with `/` create unintended S3 sub-prefixes | Sanitize: `var safeFilename = Path.GetFileName(file.FileName)` before passing to `SaveUploadAsync` |
| Nitpick | `WorkspaceUploadService.cs` | 173–177 | Binary detection uses null-byte scan instead of MIME type check — inconsistent with harness-server.js which checks `mime_type` column | Accept as-is OR align with harness: check `mimeType` before fetching from S3, fall back to null-byte scan as secondary guard |

---

## Critical Checks — All Passed

**C1 — GuidFormat / Raw MySQL:** `WorkspaceUploadService.cs` uses `IDbContextFactory<AppDbContext>` exclusively. No `MySqlConnection` or `MySqlConnectionStringBuilder` construction. ✅

**C2 — IDbContextFactory Pattern:** Constructor injects `IDbContextFactory<AppDbContext>`. Every DB method opens `await using var db = await _dbFactory.CreateDbContextAsync()`. No shared `_context` field. ✅

**C3 — 50MB Server-Side Enforcement:**
```csharp
[RequestSizeLimit(52428800)]
...
if (file.Length > 52428800)
    return StatusCode(413, new { error = "File exceeds 50MB limit" });
```
Explicit 413 in method body — not just the attribute. ✅

**C4 — S3 Path Pattern:**
```csharp
var s3Key = $"workspaces/{userId}/files/{folderId?.ToString() ?? "root"}/{filename}";
```
Matches spec. `memory/` prefix never appears in `WorkspaceUploadService.cs`. ✅

**C5 — DeleteFolderAsync S3 Before DB Delete:**
Order: (1) `CollectS3KeysRecursiveAsync` → (2) `DeleteObjectsAsync` → (3) `SaveChangesAsync`. S3 cleanup strictly precedes DB delete. ✅

**C6 — Harness userId Variable Consistency:**
`/turn` route (line 1294): `const userId = rawBody.UserId ?? rawBody.userId;`
All dispatch cases use `userId` — `list_files` (line 1876), `read_file` (line 1885), `read_memory` (line 1820), `write_memory` (line 1832), `create_document` (line 1845). ✅

**C7 — BUILTIN_TOOLS Set (lines 312–314):**
```js
const BUILTIN_TOOLS = new Set([
    'list_workspace_files', 'search_memory', 'read_memory', 'write_memory', 'create_document',
    'list_files', 'read_file'
]);
```
Both `'list_files'` and `'read_file'` present. ✅

**C8 — list_files No S3 API:** Handler queries `user_workspace_folders` and `user_workspace_uploads` via parameterized SQL only. No `ListObjectsV2`, no S3 listing, no `memory/` prefix access. ✅

---

## Important Issues — Detail

### I1: Missing FK Constraints in Migration (FAIL blocker)

**File:** `src/FortressAI.Web/Data/AppDbContext.cs` (lines 523–550) + migration `20260510194616_AddWorkspaceUploads.cs`

Neither entity has `HasOne`/`WithMany`/`OnDelete` configured in `OnModelCreating`, so EF generated no FK DDL. The migration creates both tables with correct column types and indexes, but no foreign key constraints.

**Required:**
- `user_workspace_folders.parent_id` → self with `ON DELETE CASCADE`
- `user_workspace_uploads.folder_id` → `user_workspace_folders.id` with `ON DELETE SET NULL`

**Fix — add to AppDbContext.cs:**
```csharp
modelBuilder.Entity<WorkspaceFolder>(entity =>
{
    // ... existing config unchanged ...
    entity.HasOne<WorkspaceFolder>()
          .WithMany()
          .HasForeignKey(e => e.ParentId)
          .IsRequired(false)
          .OnDelete(DeleteBehavior.Cascade);
});

modelBuilder.Entity<WorkspaceUpload>(entity =>
{
    // ... existing config unchanged ...
    entity.HasOne<WorkspaceFolder>()
          .WithMany()
          .HasForeignKey(e => e.FolderId)
          .IsRequired(false)
          .OnDelete(DeleteBehavior.SetNull);
});
```
Then: `dotnet ef migrations add AddWorkspaceUploadFKs` + `dotnet ef database update`

**Impact without fix:** Application logic in `DeleteFolderAsync` compensates correctly, but any direct DB operation or future code path that bypasses the service will leave orphaned rows. The DB has no referential integrity enforcement.

---

### A1: SaveUploadAsync — S3 Before DB (Orphan Risk)

**File:** `src/FortressAI.Web/Services/WorkspaceUploadService.cs` (lines 112, 135)

`PutObjectAsync` (line 112) fires before `db.SaveChangesAsync()` (line 135). If the DB write fails for any reason, the S3 object is uploaded with no DB record — permanently orphaned (no key stored anywhere to find and clean it up).

This is the inverse of the C5 pattern enforced for deletes, and it's inconsistent.

**Fix:**
```csharp
// Option A: DB-first
var upload = new WorkspaceUpload { ... S3Key = s3Key, ... };
db.WorkspaceUploads.Add(upload);
await db.SaveChangesAsync();  // ← DB first
await _s3.PutObjectAsync(...);  // ← S3 second

// Option B: S3 first with rollback on DB failure (current structure, add rollback)
await _s3.PutObjectAsync(...);
try {
    db.WorkspaceUploads.Add(upload);
    await db.SaveChangesAsync();
} catch {
    await _s3.DeleteObjectAsync(_bucket, s3Key);  // rollback S3 on DB failure
    throw;
}
```

---

## Other Checks — All Passed

**I2 — Loop Closure Capture (WorkspaceFiles.razor):** All `@foreach` lambdas use locally-scoped copies (`var folder = context`, `var upload = context`, `var localId = id`). No raw loop variable captured. ✅

**I3 — ReadFileContentAsync 500KB + Binary:** Both conditions implemented. (a) Null-byte scan in first 8KB returns error for binary content. (b) Truncation at 512,000 bytes appends `[Content truncated at 500KB]`. ✅ *(binary detection approach differs from harness MIME-type check — logged as nitpick)*

**I4 — Controller Auth:** All user-facing endpoints (`/folders`, `/files`, `/upload`, `/files/{id}`, `/files/{id}/download`) have `[Authorize]`. Internal-only endpoints (`/save-artifact`, `/generate-document`) correctly use `[AllowAnonymous]` + `IsInternalAuthorized()`. ✅

**N1 — ResolvePathAsync:** Implemented in `WorkspaceUploadService.cs` (lines 200–223) and mirrored in harness handlers. ✅

**N2 — Breadcrumb Trim:** `_breadcrumb.Take(idx + 1).ToList()` on click — correctly trims forward entries. ✅ *(minor edge case on non-tail duplicate navigation, documented but not blocking)*

**N3 — CSS Variables:** Zero hardcoded hex. Single inline style uses `var(--color-text-secondary)`. ✅

**N4 — IAmazonS3 Registration:** `builder.Services.AddSingleton<IAmazonS3>` at Program.cs line 124. Correct. ✅

---

## What to Fix (NEEDS-CHANGES)

Tony needs to fix **two things** before this ships:

**Fix 1 (Required — blocks merge):** Add FK relationships to `AppDbContext.cs` OnModelCreating for `WorkspaceFolder` (self-referential cascade) and `WorkspaceUpload` → `WorkspaceFolder` (set null). Generate and apply the follow-up migration. The migration `AddWorkspaceUploads` is already applied to `fait_dev` — the new migration adds only the FK constraints.

**Fix 2 (Required — data integrity):** Swap order in `SaveUploadAsync`: save the DB row before uploading to S3, or add S3 rollback if `SaveChangesAsync` throws. The current code can silently create orphaned S3 objects with no recovery path.

**Nitpick (can be bundled or follow-up):** Add `Path.GetFileName()` sanitization to `file.FileName` in `WorkspaceController` before passing to `SaveUploadAsync`.

---

*Review by Hawkeye (Clint Barton) — Cycle 1 of 2 — 2026-05-10*

---

## Review Report — ADO#3206 Cycle 2

### Verdict: PASS

---

### CC Review Summary

CC invoked with adversarial brief targeting all three Cycle 1 fixes. CC read AppDbContext.cs, WorkspaceUploadService.cs, and the new migration files directly. All fixes verified correct. No false positives.

**CC invocation:**
```bash
cd /home/fredw/projects/fip/fait && cat /tmp/clint-review-3206-c2.md | claude --model sonnet --print --dangerously-skip-permissions
```

---

### Spec Compliance

Cycle 2 verifies fixes for the three NEEDS-CHANGES items from Cycle 1. Not a full spec re-review.

---

### Fix 1 — FK Config in OnModelCreating: ✅ VERIFIED

**AppDbContext.cs lines 534–538 (WorkspaceFolder self-ref):**
```csharp
entity.HasOne<WorkspaceFolder>()
      .WithMany()
      .HasForeignKey(f => f.ParentId)
      .OnDelete(DeleteBehavior.Cascade)
      .IsRequired(false);
```

**AppDbContext.cs lines 555–559 (WorkspaceUpload → WorkspaceFolder):**
```csharp
entity.HasOne<WorkspaceFolder>()
      .WithMany()
      .HasForeignKey(u => u.FolderId)
      .OnDelete(DeleteBehavior.SetNull)
      .IsRequired(false);
```

OnDelete behaviors are **correct and not swapped**:
- Self-ref folder (ParentId) → Cascade ✅ (parent folder delete cascades to child folders)
- Upload → Folder (FolderId) → SetNull ✅ (folder delete nulls the upload's FolderId; upload is preserved)

---

### Fix 2 — S3 Rollback on DB Failure: ✅ VERIFIED

**WorkspaceUploadService.cs lines 111–146:**
```csharp
var s3Key = $"workspaces/{userId}/files/{folderId?.ToString() ?? "root"}/{safeFilename}";
await _s3.PutObjectAsync(new PutObjectRequest { BucketName = _bucket, Key = s3Key, ... });
// ...
try
{
    await db.SaveChangesAsync();
}
catch
{
    try { await _s3.DeleteObjectAsync(new DeleteObjectRequest { BucketName = _bucket, Key = s3Key }); }
    catch { /* best-effort cleanup */ }
    throw;
}
```

All sub-checks pass:
- `try/catch` wraps `SaveChangesAsync` specifically — targets the orphan scenario (S3 success → DB failure) ✅
- `DeleteObjectAsync` uses the same `s3Key` variable — no key mismatch ✅
- Inner `try/catch` around S3 delete — rollback is best-effort, won't mask the original exception ✅
- Bare `throw` re-throws the original exception ✅

---

### Fix 3 — Filename Sanitization: ✅ VERIFIED

**WorkspaceUploadService.cs lines 110–129:**
```csharp
var safeFilename = Path.GetFileName(filename);
var s3Key = $"workspaces/{userId}/files/{folderId?.ToString() ?? "root"}/{safeFilename}";
// ...
Filename = safeFilename,
S3Key = s3Key,
```

- `Path.GetFileName()` applied before S3 key construction ✅
- `safeFilename` used in S3 key ✅
- `safeFilename` used in DB `Filename` field ✅ (original `filename` never used after line 110)

**Low-risk gap noted (non-blocking):** `Path.GetFileName` on Linux only splits on `/`, not `\`. A client-supplied filename with backslashes (e.g. `..\..\..\etc\passwd`) would pass through unchanged. The fixed `workspaces/{userId}/files/` prefix on S3 keys limits real-world risk. Browser-native filenames don't contain backslashes. Not a blocking issue.

---

### Fix 4 — Migration File: ✅ VERIFIED

Migration `20260510195935_AddWorkspaceUploadsForeignKeys.cs` present in Migrations directory.

Key operations:
```csharp
migrationBuilder.AddForeignKey(
    name: "FK_user_workspace_folders_user_workspace_folders_parent_id",
    table: "user_workspace_folders",
    column: "parent_id",
    principalTable: "user_workspace_folders",
    principalColumn: "id",
    onDelete: ReferentialAction.Cascade);

migrationBuilder.AddForeignKey(
    name: "FK_user_workspace_uploads_user_workspace_folders_folder_id",
    table: "user_workspace_uploads",
    column: "folder_id",
    principalTable: "user_workspace_folders",
    principalColumn: "id",
    onDelete: ReferentialAction.SetNull);
```

- Columns `parent_id` / `folder_id` match `HasColumnName` in AppDbContext ✅
- Designer.cs and AppDbContextModelSnapshot.cs updated in same commit ✅
- `Down()` correctly drops both FKs ✅

---

### Regression Check: ✅ CLEAN

**Commit 8b9b4d3d** (3 files — all expected):
- `pipeline/REVIEW-3206-REPORT.md` — docs
- `Data/AppDbContext.cs` — FK config
- `Services/WorkspaceUploadService.cs` — S3 rollback + sanitization

**Commit 79692eb8** (3 files — all expected):
- `*_AddWorkspaceUploadsForeignKeys.cs` — migration Up/Down
- `*_AddWorkspaceUploadsForeignKeys.Designer.cs` — EF snapshot
- `Migrations/AppDbContextModelSnapshot.cs` — snapshot update

No unexpected files modified. No debug artifacts, hardcoded credentials, or TODOs found.

---

*Review by Hawkeye (Clint Barton) — Cycle 2 of 2 — 2026-05-10*
