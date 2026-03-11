# Build Report: FAIT KB Fixes v2
**Task:** FAIT-KB-FIXES-V2
**Agent:** Tony Stark (software-engineer)
**Date:** 2026-03-11
**Commit:** f7394b7
**Branch:** main

---

## Build Result

✅ **BUILD SUCCEEDED — 0 errors, 31 warnings (all pre-existing)**

```
Build succeeded.
    31 Warning(s)
    0 Error(s)
Time Elapsed 00:00:05.71
```

All warnings are pre-existing (CS8602/CS8604 nullable reference warnings in unrelated files, BedrockRuntime1002 model ID pattern, MUD0002 MudBlazor attribute warnings). Zero new warnings introduced.

---

## Files Changed

### Modified
| File | Change |
|------|--------|
| `src/FortressAI.Web/Services/KbDocumentService.cs` | PPTX→PDF conversion, diagnostic log, default IngestionStatus, removed `using System.Text;` |
| `src/FortressAI.Web/Components/Pages/KnowledgeBaseManagement.razor` | Snackbar text + help text updated (Markdown→PDF) |
| `Dockerfile` | LibreOffice added to apt-get install |

### Created
| File | Change |
|------|--------|
| `src/FortressAI.Web/Controllers/AdminKbController.cs` | New admin backfill endpoint |

---

## Fix 1a — Diagnostic Log in ListDocumentsAsync

Added `LogInformation` after the S3 listing loop in `KbDocumentService.ListDocumentsAsync`:

```csharp
_logger.LogInformation("ListDocumentsAsync: tier={Tier} userId={UserId} prefix={Prefix} → found {Count} objects",
    tier, userId, prefix, docs.Count);
```

This log will appear in CloudWatch and will confirm whether the S3 listing returns 0 for Elise's prefix (pointing to scenario A/C — wrong userId prefix) or returns her actual count.

---

## Fix 1b — Admin Backfill Endpoint

**New file:** `src/FortressAI.Web/Controllers/AdminKbController.cs`

**Endpoint:** `GET /api/kb/admin/backfill-tracking`

**Auth method:** Loopback-only IP check (same pattern as the original `resolve-user` endpoint). No auth token required. Returns `403` for any non-loopback caller.

**What it does:**
1. Paginates all objects under `kb-docs/personal/` in S3
2. Filters out `.metadata.json` files
3. Cross-references with `ProjectDocuments` table (by `S3Key`)
4. Creates tracking rows for any S3 objects missing a DB row, with `IngestionStatus = "ingested"`
5. Returns `{ backfilled: N, total: M }`

**Constructor injection:** `IAmazonS3 _s3`, `IDbContextFactory<AppDbContext> _dbFactory`, `ILogger<AdminKbController> _logger` — all already registered in DI.

---

## Fix 1c — Default IngestionStatus Changed to "ingested"

In `KbDocumentInfo` (bottom of `KbDocumentService.cs`):

```csharp
// Before:
public string IngestionStatus { get; set; } = "pending";

// After:
public string IngestionStatus { get; set; } = "ingested";  // default to ingested — no tracking row means it was uploaded before tracking was added
```

Files in S3 with no DB tracking row now correctly default to `"ingested"` status in the UI rather than showing "Processing". This is the correct assumption — files already in S3 were uploaded and ingested; the absence of a tracking row means they predate the tracking feature.

---

## Fix 2a — Dockerfile: LibreOffice Install

**Changed in `Dockerfile` (base stage):**

```dockerfile
# Before:
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*

# After:
RUN apt-get update && apt-get install -y --no-install-recommends \
    curl \
    libreoffice \
    && rm -rf /var/lib/apt/lists/*
```

Note: `libreoffice` adds ~400MB to the Docker image. This is accepted — Bedrock PDF vision quality significantly outweighs the image size cost for slide content.

---

## Fix 2b/2c — Replace ConvertPptxToMarkdown with ConvertPptxToPdfAsync

**Removed:** `ConvertPptxToMarkdown(Stream pptxStream)` — static method using `DocumentFormat.OpenXml.Packaging`, `DocumentFormat.OpenXml.Presentation`, `DocumentFormat.OpenXml.Drawing`, and `System.Text.StringBuilder`.

**Added:** `ConvertPptxToPdfAsync(Stream pptxStream, string filename, ILogger logger)` — static async method that:
1. Writes PPTX to a temp directory (`/tmp/{guid}/`)
2. Invokes `libreoffice --headless --convert-to pdf --outdir {tmpDir} {inputPath}`
3. Returns the PDF bytes on success, `null` on failure
4. Cleans up the temp directory in a `finally` block
5. Falls through gracefully — if conversion fails, the original PPTX is uploaded to S3 as-is

**Updated `UploadDocumentAsync`:** The PPTX block now calls `ConvertPptxToPdfAsync`. On success, updates `safeFilename`, `uploadStream`, `contentType` → PDF. The `key` computation uses the (potentially updated) `safeFilename`, so the PDF is stored under the correct S3 path automatically.

---

## Fix 2d — Razor Snackbar Text

Three strings updated in `KnowledgeBaseManagement.razor`:

| Location | Before | After |
|----------|--------|-------|
| Line ~89 (help text) | `PPTX auto-converted to Markdown` | `PPTX auto-converted to PDF` |
| Line ~656 (`UploadPersonalDocument`) | `Converting PPTX to Markdown for KB compatibility...` | `Converting PPTX to PDF for KB compatibility...` |
| Line ~708 (`UploadTeamDocument`) | `Converting PPTX to Markdown for KB compatibility...` | `Converting PPTX to PDF for KB compatibility...` |

---

## Fix 2e — DocumentFormat.OpenXml Usage Audit

**KbDocumentService.cs:** Had NO `using DocumentFormat.OpenXml...` directives at the file top. The library was used only via fully-qualified names inside `ConvertPptxToMarkdown`. That method is now removed. The `using System.Text;` directive (for `StringBuilder`) was also removed — `StringBuilder` was only used inside `ConvertPptxToMarkdown`.

**Other files with DocumentFormat.OpenXml usage:**
- `DocumentService.cs` — extensive usage with `using` directives; untouched
- `BedrockService.cs` — `Amazon.BedrockRuntime.DocumentFormat.Pdf` reference (different namespace, unrelated)

**Verdict:** DocumentFormat.OpenXml cleanly removed from `KbDocumentService.cs`. No other callers affected.

---

## Self-Review Checklist

- [x] All acceptance criteria implemented
- [x] Build passes with 0 errors
- [x] No new warnings introduced
- [x] `using System.Text` removed (was only used by `ConvertPptxToMarkdown`)
- [x] `DocumentFormat.OpenXml` fully removed from `KbDocumentService.cs`
- [x] `DocumentService.cs` `DocumentFormat.OpenXml` usages untouched
- [x] `AdminKbController` loopback-only auth matches pattern of existing `resolve-user` endpoint
- [x] PPTX fallthrough behavior preserved (uploads original if LibreOffice fails)
- [x] `KbDocumentInfo.IngestionStatus` default updated to `"ingested"`
- [x] Diagnostic log added to `ListDocumentsAsync`
- [x] All snackbar/help text references to "Markdown" updated to "PDF"
- [x] Committed and pushed (commit `f7394b7`)
