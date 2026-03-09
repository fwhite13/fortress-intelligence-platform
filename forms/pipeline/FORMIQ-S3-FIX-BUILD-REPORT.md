# FormIQ S3 Upload Fix

**Commit:** 5503177

## Upload path
- S3 configured (`S3:BucketName` set + `IAmazonS3` injected): saves key `formiq/uploads/{guid}.pdf` to S3 via `PutObjectAsync`, stores just the S3 key in `PdfBlobPath`
- S3 not configured: falls back to local disk path (existing behavior preserved)

## GetPdf endpoint
- Detects S3 key vs local path using existing `IsS3Key` helper: returns true if path starts with `s3://`, or has no backslash, no drive letter, and contains a forward slash
- Already handled S3 streaming before this fix — no changes needed here since the `IsS3Key` helper correctly matches keys like `formiq/uploads/{guid}.pdf`

## FormExtractionService
- Added `IAmazonS3?` optional dependency injection
- Before page count / Fortress API upload, detects S3 key: `!pdfPath.StartsWith("/") && !pdfPath.Contains(":")`
- Downloads from S3 via `GetObjectAsync` into `byte[]` before processing
- Falls back to `File.ReadAllBytesAsync` for local paths
- `fileName` derived from S3 key's last segment when reading from S3

## Files modified
- `FortressFormTools.Web/Controllers/FormsController.cs` — Upload endpoint now S3-aware, added `Amazon.S3.Model` using
- `FortressFormTools.Web/Services/FormExtractionService.cs` — Added S3 client injection, S3-aware PDF reading

## Build: 0 errors
(79 warnings — all pre-existing: NuGet version mismatches, Razor nullable annotations, MudBlazor analyzer hints)
