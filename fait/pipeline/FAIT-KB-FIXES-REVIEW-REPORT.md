# Review Report: FAIT KB Fixes — IBrowserFile Stream + PPTX Conversion

**Reviewer:** Hawkeye (Clint Barton)
**Project:** `~/projects/fip/fait/src/FortressAI.Web/`
**Commit:** `4b0c097`
**Review Cycle:** 1 of 2
**Date:** 2026-03-11

---

## Verdict: NEEDS-CHANGES

One **Critical** issue (item #13) — unhandled exception in `ConvertPptxToMarkdown` propagates through `UploadDocumentAsync` and will crash any upload of a corrupted or malformed PPTX. Everything else is clean.

---

## Checklist Results

### Fix 1 — IBrowserFile Stream

| # | Item | Result | Notes |
|---|------|--------|-------|
| 1 | `UploadPersonalDocument` reads to `byte[]` via `ReadExactlyAsync` BEFORE any SignalR-crossing `await` | ✅ PASS | Lines 648–651: `fileBytes` allocated, `rawStream` opened, `ReadExactlyAsync` called — all before `UploadDocumentAsync` |
| 2 | `UploadPersonalDocument` creates `new MemoryStream(fileBytes)` and passes to `UploadDocumentAsync` | ✅ PASS | Line 652: `await using var safeStream = new MemoryStream(fileBytes)` passed correctly |
| 3 | Same bytes-first pattern in `UploadTeamDocument` | ✅ PASS | Lines 700–704: identical pattern — `fileBytes`, `rawStream`, `ReadExactlyAsync`, `safeStream` |
| 4 | `OnInitializedAsync` S3 list failure shows `Snackbar.Add(..., Severity.Warning)` | ✅ PASS | Lines 479–482: try/catch with `Snackbar.Add("Could not load your KB documents...", Severity.Warning)` |
| 5 | `ListDocumentsAsync` has early return for `userId == Guid.Empty` on Personal tier | ✅ PASS | First line of method body: `if (userId == Guid.Empty && tier == KbTier.Personal) return new();` |
| 6 | No remaining `file.OpenReadStream()` after the fix in either upload method | ✅ PASS | `OpenReadStream(maxSize)` is still present but correctly used only to populate `fileBytes` immediately — no direct pass to service |

### Fix 2 — PPTX Conversion

| # | Item | Result | Notes |
|---|------|--------|-------|
| 7 | `UploadDocumentAsync` detects `.pptx` extension case-insensitively before S3 upload | ✅ PASS | `safeFilename.EndsWith(".pptx", StringComparison.OrdinalIgnoreCase)` — correct |
| 8 | `ConvertPptxToMarkdown` returns proper Markdown with `## Slide N` headers | ✅ PASS | `sb.AppendLine($"## Slide {slideNum}")` per slide — correct Markdown H2 headers |
| 9 | Filename changed from `.pptx` to `.md` before upload | ✅ PASS | `safeFilename = Path.ChangeExtension(safeFilename, ".md")` — correct |
| 10 | Content type changed to `text/markdown` before upload | ✅ PASS | `contentType = "text/markdown"` — correct |
| 11 | S3 key uses new `.md` filename (not original `.pptx`) | ✅ PASS | `key` is assigned AFTER `safeFilename` is mutated — `key` ends in `.md` |
| 12 | Original `fileStream` parameter NOT reassigned — separate `uploadStream` local used | ✅ PASS | `Stream uploadStream = fileStream;` then `uploadStream = new MemoryStream(...)` — `fileStream` is untouched |
| 13 | `ConvertPptxToMarkdown` has try/catch — corrupted PPTX returns fallback, not an exception | ❌ **CRITICAL** | **No try/catch present.** A corrupted/malformed PPTX causes `PresentationDocument.Open()` or any XML traversal to throw directly into `UploadDocumentAsync`, which propagates to the Razor `catch` as a raw exception with no meaningful user message. Must wrap in try/catch and return a fallback string (e.g., `"[Failed to extract PPTX content]"`). |
| 14 | `PresentationDocument` properly disposed via `using var` | ✅ PASS | `using var presentation = PresentationDocument.Open(...)` — disposed at end of method scope |
| 15 | Razor shows PPTX conversion snackbar BEFORE upload | ✅ PASS | `Snackbar.Add("Converting PPTX to Markdown...")` called BEFORE `UploadDocumentAsync` in both `UploadPersonalDocument` and `UploadTeamDocument` |
| 16 | No PPTX extension in any blocklist/validator that rejects before conversion | ✅ PASS | `DocumentService.AcceptFilter` (which the `MudFileUpload` uses) includes `.pptx` — no rejection path |

### Regression Safety

| # | Item | Result | Notes |
|---|------|--------|-------|
| 17 | Non-PPTX uploads follow exact same path — PPTX check is additive only | ✅ PASS | `if (safeFilename.EndsWith(".pptx", ...))` block is purely additive; `uploadStream` defaults to `fileStream` for all other types |
| 18 | `ListDocumentsAsync` still returns S3 listing correctly | ✅ PASS | Pagination loop with `ContinuationToken` intact; DB status join is additive post-list |
| 19 | No changes to `StartIngestionAsync`, `DeleteDocumentAsync`, or other methods | ✅ PASS | Both methods read as original; no modifications detected |

---

## Issues

### 🔴 Critical — #13: `ConvertPptxToMarkdown` Missing Exception Handling

**File:** `Services/KbDocumentService.cs`
**Method:** `ConvertPptxToMarkdown`

**Current code:**
```csharp
private static string ConvertPptxToMarkdown(Stream pptxStream)
{
    using var presentation = DocumentFormat.OpenXml.Packaging.PresentationDocument.Open(pptxStream, false);
    // ... no try/catch
}
```

**Problem:** `PresentationDocument.Open()` throws `OpenXmlPackageException` or `IOException` for corrupted, password-protected, or truncated files. The exception propagates uncaught through `UploadDocumentAsync` into the Razor `catch (Exception ex)` block, producing a user-facing "Upload failed: [OpenXml internal message]" error. More importantly, the upload is aborted entirely — the file is lost. A corrupted PPTX should upload as an empty/stub Markdown file, not crash the upload pipeline.

**Required fix:**
```csharp
private static string ConvertPptxToMarkdown(Stream pptxStream)
{
    try
    {
        using var presentation = DocumentFormat.OpenXml.Packaging.PresentationDocument.Open(pptxStream, false);
        var sb = new StringBuilder();
        var pres = presentation.PresentationPart?.Presentation;
        if (pres?.SlideIdList == null) return "[Empty presentation]";

        int slideNum = 1;
        foreach (var slideId in pres.SlideIdList.Elements<DocumentFormat.OpenXml.Presentation.SlideId>())
        {
            var rId = slideId.RelationshipId?.Value;
            if (rId == null) continue;
            var slidePart = (DocumentFormat.OpenXml.Packaging.SlidePart)presentation.PresentationPart!.GetPartById(rId);

            sb.AppendLine($"## Slide {slideNum}");

            var texts = slidePart.Slide.Descendants<DocumentFormat.OpenXml.Drawing.Text>()
                .Select(t => t.Text?.Trim())
                .Where(t => !string.IsNullOrEmpty(t))
                .Distinct()
                .ToList();

            foreach (var text in texts)
                sb.AppendLine(text);

            sb.AppendLine();
            slideNum++;
        }

        return sb.ToString();
    }
    catch (Exception)
    {
        // Corrupted or unreadable PPTX — return stub so upload proceeds
        return "[PPTX content could not be extracted — file may be corrupted or password-protected]";
    }
}
```

---

## Nitpicks (Non-Blocking)

- **`ConvertPptxToMarkdown` is `private static`** — since it receives a `Stream` (not service dependencies), this is fine. But consider logging a warning in the caller (`UploadDocumentAsync`) when the fallback string is returned, so server logs flag the failure for monitoring.
- **`FileSize = 0` in DB tracking row** — noted as `// not tracked for KB uploads` — intentional but worth a comment clarifying why it's acceptable (size is irrelevant post-conversion; S3 object size is used for display via `ListDocumentsAsync`).

---

## Summary

All 18 passing items are clean and correct. The IBrowserFile bytes-first pattern matches the `ChatView.razor` fix exactly. S3 key mutation sequence is correct (key assigned after filename change — item #11 confirmed). Snackbar before upload is correctly ordered. `ListDocumentsAsync` early-return guard and Snackbar warning on init failure are both present.

**One blocker:** `ConvertPptxToMarkdown` has no exception handling. A single corrupted PPTX will crash the upload for that user with a confusing error. Fix is straightforward — wrap body in try/catch, return fallback string.

**Fix required before PASS:**
- `KbDocumentService.cs` → `ConvertPptxToMarkdown` → add try/catch wrapping the entire method body, returning `"[PPTX content could not be extracted...]"` on exception.

---

*Hawkeye out.*
