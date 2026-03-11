# Review Report: FAIT KB Fixes v2 — LibreOffice PPTX→PDF + Backfill Endpoint

**Commit:** `f7394b7`
**Reviewer:** Hawkeye (Clint Barton) — code-reviewer
**Review Cycle:** 1 of 2
**Verdict:** ⚠️ NEEDS-CHANGES

---

## Summary

Solid implementation overall. The LibreOffice conversion path is well-structured, the backfill endpoint is idempotent and properly guarded, and the diagnostic logging is non-destructive. Two issues require fixes before merge: unquoted paths in the LibreOffice arguments string (correctness/security) and `IngestionStatus = "pending"` still hardcoded in `UploadDocumentAsync`'s tracking row (contradicts the intent of item 14).

---

## Checklist Results

### Dockerfile

| # | Item | Result | Notes |
|---|------|--------|-------|
| 1 | LibreOffice in `base` (runtime) stage | ✅ PASS | Installed in `FROM aspnet:8.0 AS base` — correct stage |
| 2 | `--no-install-recommends` present | ✅ PASS | Present on the apt-get install line |
| 3 | `apt-get clean` / `rm -rf /var/lib/apt/lists/*` | ⚠️ PARTIAL | `rm -rf /var/lib/apt/lists/*` is present; `apt-get clean` is NOT explicitly called. Not a blocker — `rm -rf` of the lists directory achieves the cache-busting goal — but explicit `apt-get clean` is a best-practice addition. Noting as nitpick. |

### PPTX→PDF Conversion

| # | Item | Result | Notes |
|---|------|--------|-------|
| 4 | `ConvertPptxToMarkdown` fully removed | ✅ PASS | Git diff confirms removal; no remaining calls or references |
| 5 | `ConvertPptxToPdfAsync` is `private static async Task<byte[]?>` | ✅ PASS | Signature confirmed |
| 6 | Temp dir uses `Guid.NewGuid()` | ✅ PASS | `Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))` |
| 7 | Temp dir cleaned up in `finally` | ✅ PASS | `Directory.Delete(tmpDir, recursive: true)` in `finally` block |
| 8 | LibreOffice args: `--headless --convert-to pdf --outdir {tmpDir} {inputPath}` | ❌ FAIL | **See Critical Issue #1 below** — paths are unquoted |
| 9 | Non-zero exit returns null | ✅ PASS | `if (proc.ExitCode != 0)` → `return null` |
| 10 | Null fallthrough uploads original PPTX | ✅ PASS | Falls through the `if (pdfBytes != null)` block; original stream/filename/contentType untouched |
| 11 | S3 key uses `.pdf` extension on success | ✅ PASS | `safeFilename = convertedFilename` (`.pdf`) before key assembly |
| 12 | `contentType = "application/pdf"` on success | ✅ PASS | Set inside the `if (pdfBytes != null)` block |

### Fix 1 — Diagnostic + Backfill

| # | Item | Result | Notes |
|---|------|--------|-------|
| 13 | `ListDocumentsAsync` log shows tier, userId, prefix, count | ✅ PASS | Log line after the `do/while` loop: `tier={Tier} userId={UserId} prefix={Prefix} → found {Count}` |
| 14 | `KbDocumentInfo.IngestionStatus` default is `"ingested"` | ✅ PASS | `public string IngestionStatus { get; set; } = "ingested"` on the DTO |
| 15 | Controller at `[Route("api/kb")]` + `[HttpGet("admin/backfill-tracking")]` | ✅ PASS | Routes confirmed |
| 16 | Backfill uses loopback IP check | ✅ PASS | IPv4-mapped IPv6 unwrap + `IPAddress.IsLoopback()` — matches pattern in `BraveSearchMcpAdapter` |
| 17 | Scans `kb-docs/personal/` prefix, skips `.metadata.json` | ✅ PASS | Prefix hardcoded; `.EndsWith(".metadata.json")` filter present |
| 18 | Backfill rows use `IngestionStatus = "ingested"` | ✅ PASS | Confirmed in `AdminKbController` |
| 19 | Idempotent — checks existing keys before insert | ✅ PASS | `existingKeys` HashSet loaded from DB; `if (existingKeys.Contains(key)) continue;` |

### Cleanup

| # | Item | Result | Notes |
|---|------|--------|-------|
| 20 | No stale `using DocumentFormat.OpenXml` or `using System.Text` | ✅ PASS | Git diff: `using System.Text` removed; `DocumentFormat.OpenXml` was inline (not a using directive) and is gone. Current file imports: `System.Text.Json` only — correct |
| 21 | Razor snackbar says "PDF" not "Markdown" in all upload methods | ✅ PASS | Both `UploadPersonalDocument` and `UploadTeamDocument` PPTX toasts updated; help text caption also updated |
| 22 | No other files accidentally modified | ✅ PASS | Git diff only touches the 4 expected files (FIRM pipeline reports in `firm/` are from a separate task, not this commit's scope) |

### Regression Safety

| # | Item | Result | Notes |
|---|------|--------|-------|
| 23 | Non-PPTX uploads follow same path | ✅ PASS | PPTX check is `if (safeFilename.EndsWith(".pptx", ...))` — fully additive; other types skip it entirely |
| 24 | `ListDocumentsAsync` return values unchanged | ✅ PASS | Log line added AFTER the loop, before the DB join. Control flow and return value unaffected |

---

## Issues

### ❌ Critical — Item 8: Unquoted paths in LibreOffice Arguments string

**File:** `src/FortressAI.Web/Services/KbDocumentService.cs`
**Line:** ~387

```csharp
// CURRENT — broken if path contains spaces
Arguments = $"--headless --convert-to pdf --outdir {tmpDir} {inputPath}",
```

`tmpDir` is built from `Path.GetTempPath()` + a GUID, so in practice it's usually space-free on Linux. However:
- `Path.GetTempPath()` on some Linux environments can return `/tmp/` which is fine, but this is not guaranteed across all deployment environments.
- More critically, **`inputPath` includes the original filename** (`filename` parameter from the caller). A filename like `"Q4 Sales Overview.pptx"` will be split into separate args by the shell-free `ProcessStartInfo`, causing LibreOffice to receive `Q4`, `Sales`, `Overview.pptx` as separate arguments and the conversion will fail silently (exit code may or may not be 0 depending on LibreOffice version).
- On Fargate, the temp path is typically `/tmp/` but the filename is user-controlled input.

**Required fix — quote the path arguments:**
```csharp
Arguments = $"--headless --convert-to pdf --outdir \"{tmpDir}\" \"{inputPath}\"",
```

**Why not `ArgumentList`?** Could also use `ArgumentList` (correct approach for truly arbitrary paths) but quoting is the minimal surgical fix here and consistent with the existing style.

---

### ❌ Important — Item 14 (partial): `UploadDocumentAsync` still inserts `"pending"` tracking rows

**File:** `src/FortressAI.Web/Services/KbDocumentService.cs`
**Line:** ~121

```csharp
// CURRENT
IngestionStatus = "pending",
```

The checklist item 14 passes for `KbDocumentInfo.IngestionStatus` (the DTO default), but the tracking row inserted at upload time still uses `"pending"`. This is inconsistent with the intent of this PR:

- The backfill endpoint sets newly discovered rows to `"ingested"` (correct — files that existed before tracking are already ingested).
- But freshly uploaded files go in as `"pending"` which is semantically correct (Bedrock hasn't ingested them yet), so this is **not a bug per se**.
- However: the checklist item 14's stated rationale is "no tracking row means it was uploaded before tracking was added." The DTO default of `"ingested"` handles the case where S3 has a file but the DB has no row. The upload path inserting `"pending"` is actually the *right* behavior for new uploads — they genuinely are pending until Bedrock confirms ingestion.
- **Verdict:** The `"pending"` in `UploadDocumentAsync` is correct and intentional. The DTO default of `"ingested"` handles the different case (legacy files with no DB row). These two are consistent when you understand the intent. **This is not a bug — reclassifying to nitpick.**

**Reclassification: Nitpick (no fix required)** — but the comment on the DTO could be clearer to avoid future confusion:
```csharp
// Default "ingested" only applies when no DB row exists for a legacy S3 file.
// New uploads are inserted with "pending" and updated by KbSyncRetryService.
public string IngestionStatus { get; set; } = "ingested";
```

---

### ⚠️ Nitpick — Item 3: `apt-get clean` missing from Dockerfile

**File:** `Dockerfile`

`rm -rf /var/lib/apt/lists/*` is present and handles the most important part (removes package lists). Adding `apt-get clean` before the `rm -rf` is best practice to also clear the local package cache (`/var/cache/apt/archives/`).

```dockerfile
# Recommended
RUN apt-get update && apt-get install -y --no-install-recommends \
    curl \
    libreoffice \
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/*
```

Note: LibreOffice is large (~300MB+). The `apt-get clean` step would reduce the image layer by whatever was cached in `/var/cache/apt/archives/` during install. Worth doing given the image size impact of adding LibreOffice.

---

### ⚠️ Nitpick — Item 8 (secondary): `stderr` read after `WaitForExitAsync`

**File:** `src/FortressAI.Web/Services/KbDocumentService.cs`

```csharp
await proc.WaitForExitAsync();

if (proc.ExitCode != 0)
{
    var err = await proc.StandardError.ReadToEndAsync();
```

`StandardError` is read **after** `WaitForExitAsync()`. On a process with large stderr output, this can deadlock — the process fills the stderr pipe buffer and blocks waiting for the reader, but `WaitForExitAsync()` is waiting for the process to exit. LibreOffice's stderr is typically small, so this is unlikely to manifest in practice, but it's a known async process anti-pattern.

**Recommended fix:** Read stdout/stderr concurrently before waiting, or redirect to null if not needed:
```csharp
var stderrTask = proc.StandardError.ReadToEndAsync();
await proc.WaitForExitAsync();
var err = await stderrTask;
```

---

## Summary of Required Changes

| Priority | Item | File | Fix |
|----------|------|------|-----|
| ❌ Critical | Quote paths in LibreOffice Arguments | `KbDocumentService.cs` ~L387 | Wrap `{tmpDir}` and `{inputPath}` in double-quotes |
| ⚠️ Nitpick | `apt-get clean` in Dockerfile | `Dockerfile` L7 | Add `&& apt-get clean \` before the `rm -rf` line |
| ⚠️ Nitpick | `stderr` read after `WaitForExitAsync` | `KbDocumentService.cs` ~L399 | Read stderr concurrently |
| ℹ️ Clarity | DTO comment could clarify two-case design | `KbDocumentService.cs` ~L432 | Update comment (no code change needed) |

---

## Focus Item Verification

| Focus Item | Status |
|---|---|
| **#1** LibreOffice in runtime stage | ✅ Confirmed in `base` stage |
| **#7** Temp dir cleanup in `finally` | ✅ Confirmed |
| **#10** Null fallthrough preserves PPTX | ✅ Confirmed — graceful degradation works correctly |
| **#16** Loopback check on backfill | ✅ Confirmed — matches established pattern |

---

## Verdict

**NEEDS-CHANGES**

One critical fix required before merge: quote the path arguments in the LibreOffice `ProcessStartInfo.Arguments` string. A filename with spaces (entirely normal user input) will silently fail conversion. The `apt-get clean` nitpick is worth fixing in the same pass given LibreOffice's image size impact. The stderr deadlock risk is low but easy to fix.

Everything else is solid. The temp directory lifecycle, null fallthrough, S3 key/contentType handling, idempotent backfill, and loopback guard are all correctly implemented.

---

*Review by Hawkeye — code-reviewer agent*
*Pipeline: FAIT-KB-FIXES-V2 | Cycle 1 of 2*
