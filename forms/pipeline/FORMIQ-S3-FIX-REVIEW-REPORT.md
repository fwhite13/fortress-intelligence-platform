# Review Report: FORMIQ-S3-FIX

**Reviewer:** Hawkeye (Code Reviewer)
**Date:** 2026-02-27
**Commit:** `5503177`
**Priority:** CRITICAL — demo blocker
**Requested by:** Maria Hill

---

## Verdict: NEEDS-CHANGES

One Important issue: `IsS3Key` in `FormsController` incorrectly classifies Linux absolute paths as S3 keys, and is **inconsistent** with the correct check used in `FormExtractionService`. Not a hard demo blocker on a fresh deployment (no legacy local-path records), but a real bug in mixed environments. Easy one-line fix.

Everything else checks out cleanly.

---

## Consistency Audit

**Files Cross-Referenced:**

| Cross-check | Result |
|---|---|
| `_config["S3:BucketName"]` in `FormsController` ↔ `FormExtractionService` ↔ `appsettings.json` key `S3.BucketName` | ✅ All use `"S3:BucketName"` — consistent |
| `IAmazonS3? s3 = null` in `FormExtractionService` constructor ↔ null-guard pattern (`_s3 != null`) in all S3 branches | ✅ Consistent |
| S3 key format stored in `PdfBlobPath` (`formiq/uploads/{guid}.pdf`) ↔ key used in `GetObjectAsync` in `FormExtractionService` ↔ key extraction in `GetPdf` | ✅ Consistent — no bucket name embedded |
| `IsS3Key` logic in `FormsController` ↔ inline S3 detection in `FormExtractionService` | ❌ **MISMATCH** (see I1) |
| `PutObjectRequest.BucketName` ↔ `_config["S3:BucketName"]` (not hardcoded) | ✅ Consistent |
| `AWSSDK.S3` package in `FortressFormTools.Web.deps.json` ↔ `using Amazon.S3` / `using Amazon.S3.Model` in source | ✅ Consistent |

---

## Critical Issues — 0

None.

---

## Important Issues — 1

### I1: `IsS3Key` in `FormsController` incorrectly matches Linux absolute local paths

- **File:** `FortressFormTools.Web/Controllers/FormsController.cs` (lines 290–296)
- **Category:** Correctness + Consistency mismatch with `FormExtractionService`
- **Issue:** The `IsS3Key` helper's heuristic — "no backslash, no drive letter, contains forward slash" — does **not** exclude Linux absolute paths like `/app/uploads/abc.pdf`. Such a path:
  - Has no backslash ✓
  - Has no drive letter (`[A-Za-z]:`) ✓  
  - Contains a forward slash ✓
  
  → `IsS3Key("/app/uploads/abc.pdf")` returns **true** (incorrect).

  When S3 is configured, `GetPdf` evaluates `IsS3Key` before the local-file fallback. If it returns true, the code attempts `_s3.GetObjectAsync(bucket, "/app/uploads/abc.pdf")` → S3 `NoSuchKey` → returns `NotFound("PDF file not found in S3")`. The local file fallback at the bottom of the method is **unreachable** once the S3 branch is entered. Legacy local-path records silently fail to serve.

  `FormExtractionService` uses **different** (correct) inline logic:
  ```csharp
  // FormExtractionService — CORRECT
  bool isS3Key = !string.IsNullOrEmpty(s3BucketName) && _s3 != null
      && !pdfPath.StartsWith("/") && !pdfPath.Contains(":");
  ```
  The `!pdfPath.StartsWith("/")` check correctly excludes Linux absolute paths. `FormsController.IsS3Key` is missing this guard.

**Evidence:**
```csharp
// FormsController.cs — IsS3Key (BUGGY)
private static bool IsS3Key(string path)
{
    if (string.IsNullOrEmpty(path)) return false;
    if (path.StartsWith("s3://")) return true;
    // "Not a local path: no backslash, no drive letter, contains forward slash"
    // ↑ This comment is WRONG — Linux absolute paths satisfy all three conditions
    return !path.Contains('\\') && !System.Text.RegularExpressions.Regex.IsMatch(path, @"^[A-Za-z]:") && path.Contains('/');
}
```

- **Impact:** Any `FormLibrary` record with a local-path `PdfBlobPath` (e.g. created before S3 was enabled) will return `NotFound` from `GetPdf` when S3 is configured. For a fresh demo deployment where all records are created after S3 is enabled, this won't trigger — but it's a latent data-loss hazard for any migration or mixed scenario.

- **Fix:** Add `!path.StartsWith("/")` to match the `FormExtractionService` logic:
  ```diff
  - return !path.Contains('\\') && !System.Text.RegularExpressions.Regex.IsMatch(path, @"^[A-Za-z]:") && path.Contains('/');
  + return !path.StartsWith("/") && !path.Contains('\\') && !System.Text.RegularExpressions.Regex.IsMatch(path, @"^[A-Za-z]:") && path.Contains('/');
  ```
  This makes `IsS3Key` consistent with `FormExtractionService` and correctly rejects Linux absolute paths.

---

## Nitpicks — 1

**N1:** `MemoryStream` in `GetPdf`'s S3 branch is not wrapped in a `using` — `s3Response.ResponseStream` is passed directly to `File(...)` and the MVC framework takes ownership. This is the correct pattern for streaming responses; no change needed. Noted only to explain the asymmetry with the upload path. Not a bug. — Not blocking.

---

## Upload Endpoint Checklist

| Check | Result |
|---|---|
| `PutObjectAsync` uses `s3BucketName` from config (not hardcoded) | ✅ `BucketName = s3BucketName` where `s3BucketName = _config["S3:BucketName"]` |
| `MemoryStream` disposed properly | ✅ `using var ms = new MemoryStream();` — C# 8 using declaration, disposed at end of block |
| Stores just the key (`formiq/uploads/{guid}.pdf`) not full URI | ✅ `pdfBlobPath = s3Key` where `s3Key = $"formiq/uploads/{fileGuid}.pdf"` |
| Fallback to local disk when S3 not configured | ✅ Clean `else` branch — existing behavior preserved, `pdfBlobPath = localPath` |

## GetPdf Checklist

| Check | Result |
|---|---|
| `IsS3Key` correctly distinguishes S3 key from local path | ❌ See I1 — Linux absolute paths incorrectly match |
| Fetches from S3 using stored key + configured bucket | ✅ `GetObjectAsync(s3Bucket, key)` where key is the raw stored value (no bucket embedded) |
| Key extraction handles stored key format | ✅ Stored key `formiq/uploads/abc.pdf` doesn't match `s3://{bucket}/` prefix → used as-is |

## FormExtractionService Checklist

| Check | Result |
|---|---|
| `IAmazonS3?` injected as optional (null-safe) | ✅ `IAmazonS3? s3 = null` — default null, all S3 branches guarded with `_s3 != null` |
| S3 download happens before PdfPig page count | ✅ `pdfBytes` populated first, then `PdfDocument.Open(pdfBytes)` |
| S3 download happens before Fortress API upload | ✅ `pdfBytes` populated first, then `_fortressClient.UploadFileAsync(..., pdfBytes, ...)` |
| Downloaded `byte[]` used consistently | ✅ `pdfBytes` from S3 stream used for both page count and Fortress upload — no mixing with `ReadAllBytes` |

## Security Checklist

| Check | Result |
|---|---|
| No hardcoded bucket names | ✅ All bucket references via `_config["S3:BucketName"]` |
| No hardcoded AWS credentials | ✅ `AmazonS3Client` uses default credential provider chain (env vars → IAM role) |
| No secrets in `appsettings.json` | ✅ `BucketName: ""` placeholder — credentials not in config |

---

## Acceptance Criteria Verification

| Criterion | Result |
|---|---|
| Upload stores S3 key (not full URI) in `PdfBlobPath` | ✅ Confirmed — `pdfBlobPath = s3Key` (`formiq/uploads/{guid}.pdf`) |
| Upload uses bucket name from config | ✅ Confirmed |
| MemoryStream properly disposed | ✅ Confirmed |
| Local fallback when S3 not configured | ✅ Confirmed |
| FormExtractionService reads from S3 before processing | ✅ Confirmed — download → PdfPig → Fortress upload |
| Optional S3 injection — no crash when not configured | ✅ Confirmed |
| `byte[]` consistency (no mixed reads) | ✅ Confirmed |
| No hardcoded credentials or bucket names | ✅ Confirmed |

---

## Positive Observations

- **Clean optional injection pattern.** `IAmazonS3? s3 = null` with `_s3 != null` guards everywhere — graceful degradation to local storage works correctly in both the controller and the extraction service.
- **MemoryStream handling on upload is correct.** `ms.Position = 0` before passing to `PutObjectRequest.InputStream` is easy to miss and it's here. Good.
- **S3 key format is clean.** `formiq/uploads/{guid}.pdf` — no bucket name embedded, no `s3://` prefix — simple and unambiguous for the stored-key-only convention.
- **FormExtractionService S3 detection logic is correct.** `!pdfPath.StartsWith("/") && !pdfPath.Contains(":")` is the right heuristic for this path convention. The controller's `IsS3Key` just needs to be brought into alignment.
- **Error handling in GetPdf is complete.** S3 failure is caught and returns `NotFound` with a clear message — no unhandled exceptions.

---

## Required Action

**One fix needed before PASS:**

**I1:** Add `!path.StartsWith("/")` to `IsS3Key` in `FormsController.cs`:
```diff
- return !path.Contains('\\') && !System.Text.RegularExpressions.Regex.IsMatch(path, @"^[A-Za-z]:") && path.Contains('/');
+ return !path.StartsWith("/") && !path.Contains('\\') && !System.Text.RegularExpressions.Regex.IsMatch(path, @"^[A-Za-z]:") && path.Contains('/');
```
One line, no risk, makes it consistent with `FormExtractionService`. Demo is safe as-is (fresh deployment), but this should be clean before any data migration.

---

_Reviewed by Hawkeye — you see what others miss._
