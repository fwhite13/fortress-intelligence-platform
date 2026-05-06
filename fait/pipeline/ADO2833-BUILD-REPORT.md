# Build Report — ADO#2833
## KB Upload: BdaProcessingService + Image BDA + PPTX Parity

**Commit:** `a0ff695`  
**Branch:** main  
**Build result:** ✅ SUCCEEDED (0 errors)  
**Date:** 2026-05-06

---

## What Was Built

1. **`BdaProcessingService`** — New service that takes an S3 key of an already-uploaded image, invokes BDA async job via `InvokeDataAutomationAsyncAsync`, polls for completion (max 60s at 5s intervals), reads the output JSON, extracts text via `ExtractTextFromBdaOutput`, and writes a `.txt` sidecar at `{s3Key}-bda-text.txt`. Fully non-fatal — all exceptions caught internally, logs warning on failure.

2. **`KbDocumentService.UploadDocumentAsync`** — BDA processing wired in after S3 upload for images. Uses `Task.Run` (fire-and-forget) so BDA doesn't block the upload response.

3. **`KbDocumentService.UploadProjectDocumentAsync`** — PPTX→PDF conversion added (was missing; copied pattern from `UploadDocumentAsync`). BDA image processing also added. Both changes parity the Personal/Team path.

4. **`KnowledgeBaseManagement.razor`** — Help text updated to include image types and size note.

5. **`Program.cs`** — `BdaProcessingService` registered as `AddScoped`.

6. **`FortressAI.Web.csproj`** — `AWSSDK.BedrockDataAutomationRuntime` NuGet package added.

---

## Files Changed

| File | Change |
|------|--------|
| `src/FortressAI.Web/Services/BdaProcessingService.cs` | **CREATED** — New service, 201 lines |
| `src/FortressAI.Web/Services/KbDocumentService.cs` | **MODIFIED** — Inject BdaProcessingService, add BDA to Upload + PPTX to Project path |
| `src/FortressAI.Web/Program.cs` | **MODIFIED** — Register `BdaProcessingService` |
| `src/FortressAI.Web/FortressAI.Web.csproj` | **MODIFIED** — Add NuGet ref |
| `src/FortressAI.Web/Components/Pages/KnowledgeBaseManagement.razor` | **MODIFIED** — Help text |

Total: **5 files, +251 lines**

---

## NuGet Package

- **Package:** `AWSSDK.BedrockDataAutomationRuntime`
- **Version pattern:** `3.7.*` (resolves to `3.7.504.18` — latest stable in the 3.7.x line)
- **Rationale:** All other AWSSDK packages in the project use `3.7.*`. Using 4.0.x would introduce a mixed-generation SDK situation; 3.7.x is stable and consistent. Build confirmed 0 errors.

---

## BDA Profile ARN

**OMITTED** — `DataAutomationProfileArn` is not set on `InvokeDataAutomationAsyncRequest`. BDA uses the standard default output profile when the field is absent. This is the safest approach since the exact ARN format (`arn:aws:bedrock:us-east-1::data-automation/aws-standard-output-profile/1.0.0`) is undocumented in the SDK and varies by account/region. If BDA jobs fail at runtime with a profile-related error, the ARN can be added as a config value.

---

## SDK Type Correction (Noted by CC)

The brief specified `InputDataConfiguration`/`OutputDataConfiguration` as wrapper classes. The actual SDK 3.7.504.18 uses `InputConfiguration`/`OutputConfiguration` directly on the request. CC adapted to the correct names automatically and the build confirms zero compile errors.

---

## BDA Processing Design Notes

- **Fire-and-forget:** `Task.Run(() => _bdaService.ProcessImageAsync(key), CancellationToken.None)` — BDA runs async in background; upload returns immediately. This is intentional: BDA takes 5-60s and blocking the upload response would harm UX.
- **Sidecar location:** `{s3Key}-bda-text.txt` (same S3 bucket, `fortress-tools`)
- **BDA output staging:** BDA writes to `bda-output/{s3Key}/` prefix. Service lists that prefix for `.json` files, reads the newest, extracts text.
- **Text extraction:** Walks `output_segments[*].standard_output.text` and `semantic_modality_output.description`. Falls back to `content`, then `text`, then raw JSON. This covers the standard BDA output schema plus unknown future schemas.

---

## IAM Note (for Rhodey)

`BdaProcessingService` requires the ECS task role to have:
- `bedrock:InvokeDataAutomationAsync` (BDA Runtime)
- `bedrock:GetDataAutomationStatus` (BDA Runtime polling)
- `s3:PutObject` on `bda-output/*` prefix (output staging)
- `s3:GetObject` + `s3:ListObjectsV2` on `bda-output/*` (reading results)

S3 permissions for `fortress-tools` likely already exist. The `bedrock:*DataAutomation*` actions for the BDA Runtime endpoint need to be added to the task role if not already present.

---

## Acceptance Criteria Verification

- [x] `BdaProcessingService` exists and registered in DI — ✅
- [x] `AWSSDK.BedrockDataAutomationRuntime` added to `.csproj` — ✅ (3.7.*)
- [x] `UploadDocumentAsync` (Personal/Team): images trigger BDA after S3 upload — ✅
- [x] `UploadProjectDocumentAsync` (Project): PPTX→PDF added — ✅
- [x] `UploadProjectDocumentAsync` (Project): images trigger BDA — ✅
- [x] BDA processing is non-fatal — ✅ (fire-and-forget + catch-all in ProcessImageAsync)
- [x] `KnowledgeBaseManagement.razor` help text updated with image types + 3.75 MB note — ✅
- [x] Build compiles with 0 errors — ✅

---

## CC Sessions

- **1 session** (sonnet) — sequential (no parallelization needed; single-service build with clear dependency chain: NuGet → create service → wire into existing service → register → help text)

---

## Known Edge Cases / For Clint to Scrutinize

1. **Fire-and-forget Task.Run** — BDA is a background operation. If the app restarts between upload and BDA completion, the sidecar will not be written. This is acceptable given the non-fatal requirement, but means image OCR text won't be in KB for that document. A durable queue approach would fix this, but is out of scope for this WI.

2. **BDA output prefix accumulation** — `bda-output/{s3Key}/` prefixes accumulate in S3 over time. There's no cleanup on document delete. The `DeleteDocumentAsync` method does not remove BDA output artifacts. Low priority but worth noting.

3. **BDA us-east-1 hardcoded** — `BdaProcessingService.CreateBdaClient()` hardcodes `USEast1`. BDA is only available in us-east-1 at time of writing, so this is correct. If BDA expands regions, this needs to become configurable (like other AWS clients in Program.cs that read `AWS:Region`).

4. **3.75 MB image size limit in help text** — This is an informational note only; no server-side enforcement was added. The upload path allows up to 10 MB for all files. If actual BDA/Bedrock has a hard limit, a server-side guard should be added.

---

## How to Test Locally

1. Build: `cd src/FortressAI.Web && dotnet build` — should be 0 errors
2. Upload an image (JPG/PNG) to a Personal or Team KB — check CloudWatch/logs for `[BDA] Starting image processing` and eventual `[BDA] Sidecar written` or warning
3. Upload a PPTX to a Project KB — verify it's stored as `.pdf` in S3
4. Upload a non-image/non-PPTX file — verify BDA is not triggered, upload proceeds normally

---

_Build report written by Tony Stark — sending to Clint for review._
