# FormIQ Mega Sprint — Build Report

**Date:** 2026-02-27
**Commit:** 21aa843

## P1: Async Extraction Polling
- **Already implemented** — the codebase already has the full async submit→poll pattern:
  - `FortressProjectsClient` has `SubmitRequestAsync()` → returns `projectRequestId`
  - `GetRequestStatusAsync()` polls `/clients/{ClientId}/projects/{ProjectId}/requests/{requestId}`
  - `FormExtractionService` calls these in sequence with polling
  - `ExtractionBackgroundService` runs in background, dequeued from the upload endpoint
  - Upload endpoint returns immediately with "Queued" status
- No changes were needed — the architecture already prevents gateway timeout

## P2: Delete Forms
- DELETE /api/forms/{id}: ✅
  - Cascade deletes FormFields + FieldCorrections via EF
  - Deletes local PDF file or S3 object (with P4 integration)
  - Returns 204 on success, 404 if not found
- FormLibrary delete button: ✅ (trash icon per row with confirmation dialog)
- FormDetail delete button: ✅ (Delete button in header bar with confirmation → navigates back)

## P3: Resubmit Failed
- POST /api/forms/{id}/resubmit: ✅
  - Clears existing fields, resets status to Processing, re-enqueues
  - Returns 202 Accepted
  - Rejects if already Processing
- Retry button on error rows: ✅ (refresh icon, Color=Warning, in FormLibrary)
- Retry button in FormDetail: ✅ (shown when status is Error)

## P4: S3 Storage
- S3 bucket config key: `S3:BucketName` (in appsettings.json, overridable via env `S3__BucketName`)
- S3 region config: `S3:Region` (default: us-east-1)
- AWSSDK.S3 added: ✅
- IAmazonS3 registered as singleton (uses default credentials — ECS task role)
- GET /api/forms/{id}/pdf: streams from S3 if BucketName is configured and path looks like S3 key
- DELETE: deletes S3 object if applicable
- **Backward compat:** If S3:BucketName is empty, falls back to local file storage. S3 detection uses heuristic: `s3://` prefix or path with `/` but no `\` or drive letter
- Upload flow still stores local path by default; when S3 is configured, the Fortress API presigned URL flow should store the S3 key in PdfBlobPath

## P5: Synonyms UI
- Table chip display: ✅ (first 3 as chips + "+N more" indicator)
- Dialog input field: ✅ (MudTextField with comma-separated helper text)
- Create/Edit flows pass Synonyms to API: ✅

## P6: Seed Script
- File: `scripts/seed-len-conventions.py`
- Field codes: 16 entries
- Uses pymysql, UPSERT pattern (check + update/insert)
- Auto-adds IsSensitive column if missing (ALTER TABLE)
- Sets IsSensitive=True for DVR_LIC and VEHICLE_VIN
- Includes all naming conventions: UPPER_SNAKE, lower_snake, PascalCase

## P7: IsSensitive
- Entity field: ✅ (`public bool IsSensitive { get; set; }` on DictionaryField)
- UI lock icon: ✅ (Lock icon in Sensitive column, Color=Error)
- Dialog toggle: ✅ (MudSwitch with label "Sensitive Data (PII/PHI)")
- DictionaryController: ✅ (create/update both handle IsSensitive)
- Note: For existing databases, the seed script handles the ALTER TABLE. New databases get the column via EnsureCreated.

## P8: Convention Note
- MudAlert added: ✅ (Severity=Info, Dense=true, between header and search bar)
- Text explains UPPER_SNAKE, lower_snake, and PascalCase conventions with examples

## Build Result
- `dotnet build`: ✅ 0 errors (79 warnings — all pre-existing MudBlazor v7 attribute warnings)

## Notes for Review
1. **P1 was already done** — the async submit→poll pattern was already fully implemented in `FortressProjectsClient` + `FormExtractionService` + `ExtractionBackgroundService`. No code changes needed.
2. **S3 detection heuristic** (P4): Uses path pattern matching to distinguish S3 keys from local paths. The presigned upload flow already sends PDFs to S3 — Clint should verify that `PdfBlobPath` stores the S3 key after upload (not a local copy path).
3. **IsSensitive column** (P7): Existing databases need an ALTER TABLE or the seed script handles it. The `IsSensitive` property defaults to `false`, so existing rows are unaffected.
4. **IAmazonS3 is optional** — injected as nullable (`IAmazonS3? s3 = null`). When S3 isn't configured, the service simply isn't registered and the controller falls back to local file access.

**DO NOT deploy — Clint reviews first.**
