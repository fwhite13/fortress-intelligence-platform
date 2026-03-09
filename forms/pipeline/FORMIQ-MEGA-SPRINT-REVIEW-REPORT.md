# Review Report: FORMIQ-MEGA-SPRINT

**Reviewer:** Hawkeye (Code Reviewer)
**Date:** 2026-02-27
**Commit:** b8897f8
**Priority:** HIGH

---

## Verdict: NEEDS-CHANGES

One Important issue in the S3 key parsing logic that will silently break PDF retrieval and delete when S3 is enabled. No security issues. Two nitpicks. Everything else is solid.

---

## Consistency Audit

**Files Cross-Referenced:**

| Cross-check | Result |
|---|---|
| `DictionaryField.IsSensitive` entity ↔ `DictionaryFieldRequest.IsSensitive` ↔ `DictionaryController` update | ✅ All three in sync |
| `DictionaryController` returns full entity (including `IsSensitive`) ↔ `DataDictionary.razor` reads `context.IsSensitive` | ✅ Match |
| `DictionaryFieldDialog.DictionaryFieldModel.IsSensitive` ↔ POST/PUT payload `isSensitive` ↔ controller accepts `DictionaryFieldRequest.IsSensitive` | ✅ Match |
| `FormsController.DeleteForm` cascade behavior ↔ `AppDbContext` EF cascade config | ✅ `DeleteBehavior.Cascade` confirmed on FormLibrary→FormField and FormField→FieldCorrection |
| `S3:BucketName` config key ↔ `_config["S3:BucketName"]` in controller ↔ `builder.Configuration["S3:BucketName"]` in Program.cs | ✅ Consistent |
| Seed script FIELDS list count (16) ↔ build report claim (16) | ✅ Confirmed 16 entries |
| `MudAlert` position in DataDictionary.razor | ✅ After header MudGrid, before search MudPaper, before MudTable |

**Undocumented Dependencies Checked:**
- `DictionaryField` entity directly used (not a DTO) in `DataDictionary.razor` — acceptable for read-only list, entity includes `IsSensitive`. ✅
- `IAmazonS3?` injected as nullable in controller — constructor defaults to `null`, no DI crash when S3 not configured. ✅

---

## Critical Issues — 0

None found.

---

## Important Issues — 1

### I1: S3 `s3://` URI key parsing includes bucket name in the key

- **Files:** `FormsController.cs` — `GetPdf` (line ~215) and `DeleteForm` (line ~243)
- **Category:** Correctness
- **Issue:** When `PdfBlobPath` starts with `s3://`, the code strips the prefix with `Substring(5)`. The standard S3 URI format is `s3://bucket-name/object-key`. `Substring(5)` removes only `s3://`, leaving `bucket-name/object-key` — which is then used as the object key with the separately-configured bucket name. The result is a malformed key like `my-bucket/uploads/abc123.pdf` when it should be `uploads/abc123.pdf`.

**Evidence:**
```csharp
// GetPdf
var key = path.StartsWith("s3://") ? path.Substring(5) : path;
// If path = "s3://my-actual-bucket/uploads/abc123.pdf"
// key = "my-actual-bucket/uploads/abc123.pdf"  ← WRONG
// s3Response = _s3.GetObjectAsync("my-configured-bucket", "my-actual-bucket/uploads/abc123.pdf")  ← NoSuchKey
```

Same pattern repeated in `DeleteForm`:
```csharp
var key = form.PdfBlobPath.StartsWith("s3://") ? form.PdfBlobPath.Substring(5) : form.PdfBlobPath;
```

- **Impact:** If any code path populates `PdfBlobPath` using standard `s3://bucket/key` URIs, `GetPdf` returns 404 and `DeleteForm` silently fails to delete the S3 object (leaves orphaned objects). S3 is currently disabled (BucketName empty), but this will surface the moment it's turned on.

- **Fix:** Two acceptable options:

  **Option A** — Strip the bucket component from `s3://` URIs (if standard S3 URI format will be used):
  ```csharp
  private static string ExtractS3Key(string path)
  {
      if (!path.StartsWith("s3://")) return path;
      // s3://bucket-name/key-path → key-path
      var withoutScheme = path.Substring(5); // remove "s3://"
      var slashIdx = withoutScheme.IndexOf('/');
      return slashIdx >= 0 ? withoutScheme.Substring(slashIdx + 1) : withoutScheme;
  }
  ```

  **Option B** — Document and enforce that `s3://` prefix in `PdfBlobPath` is NOT standard URI format — bucket name is never embedded (i.e., `s3://my-key.pdf` not `s3://bucket/my-key.pdf`). Add a code comment and confirm with whoever populates `PdfBlobPath` in the presigned upload flow.

  Option A is safer and future-proof. Option B is fine if the convention is locked down.

---

## Nitpicks — 2

**N1:** `ResubmitForm` removes `FormFields` without `.ThenInclude(ff => ff.Corrections)`. Works correctly because the EF schema has `ON DELETE CASCADE` on FieldCorrections→FormFields (DB handles it), but the intent is obscured. The `DeleteForm` method correctly loads with ThenInclude — resubmit could match that pattern for clarity.
(`FormsController.cs` — `ResubmitForm`) — Not blocking.

**N2:** `appsettings.json` has `Password=changeme` in the connection string. Standard dev practice, but worth confirming that production uses an env-var override (`ConnectionStrings__Default`) or a secrets manager. The S3 section correctly ships with empty `BucketName`, which is the right default. — Not blocking.

---

## Positive Observations

- **S3 security is correct.** `AmazonS3Client` instantiated with no explicit credentials → uses the default credential provider chain (env vars → ECS task role → EC2 instance profile). No hardcoded keys anywhere. ✅

- **IAmazonS3 optional injection is clean.** Program.cs only registers the service when `S3:BucketName` is configured; controller takes `IAmazonS3? s3 = null`; all S3 paths check both `s3Bucket` and `_s3 != null`. Graceful degradation to local storage works correctly.

- **EF cascade is properly configured.** `AppDbContext` has explicit `DeleteBehavior.Cascade` on both `FormLibrary→FormField` and `FormField→FieldCorrection`. `DeleteForm` loading with `ThenInclude(ff => ff.Corrections)` keeps EF change tracker fully informed. No orphan risk.

- **Resubmit field clearing is correct.** `_db.FormFields.RemoveRange(form.Fields)` + `SaveChangesAsync()` before enqueue ensures no duplicate field accumulation across retry cycles. DB cascade handles any FieldCorrections.

- **Synonyms whitespace handling is correct.** `StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries` in DataDictionary.razor covers the trim case properly.

- **Seed script UPSERT pattern is solid.** SELECT first by FieldCode, then UPDATE or INSERT. No duplicates. Uses env vars for all connection parameters.

- **IsSensitive is fully wired end-to-end.** Entity, DictionaryFieldRequest DTO, controller Create and Update both set it, API GET returns the full entity, UI reads it and shows lock icon. The dialog dialog's `MudSwitch` round-trips correctly.

- **MudAlert placement is correct.** Positioned below the header/add-button grid and above the search bar — exactly the logical spot for a convention note.

---

## Acceptance Criteria Verification

| # | Criterion | Result |
|---|---|---|
| P2 | `DELETE /api/forms/{id}` cascade-deletes FormFields + FieldCorrections | ✅ EF cascade confirmed in AppDbContext |
| P2 | Local file delete wrapped in try/catch | ✅ `File.Exists` check + catch with `LogWarning` |
| P2 | S3 delete key parsing correct | ⚠️ See I1 — key parsing bug for `s3://` prefix format |
| P2 | No crash when S3 not configured | ✅ Null-guard + empty-string check before any S3 call |
| P2 | UI confirmation dialog on delete | ✅ `ShowMessageBox` in both FormLibrary and FormDetail |
| P2 | FormDetail navigate to /forms after delete | ✅ `Nav.NavigateTo("/forms")` |
| P3 | Resubmit clears existing fields before re-queue | ✅ `RemoveRange` + `SaveChangesAsync` before `Enqueue` |
| P3 | Re-enqueue calls background service | ✅ `_extractionQueue.Enqueue(form.Id)` |
| P3 | UI retry button updates row status immediately | ✅ `form.Status = "Processing"` + `StateHasChanged()` inline |
| P4 | IAmazonS3 uses default credential chain (NOT hardcoded) | ✅ `new AmazonS3Client(region)` — no credential args |
| P4 | S3 bucket from config, not hardcoded | ✅ `_config["S3:BucketName"]` everywhere |
| P4 | Fallback to local storage when S3 not configured | ✅ Clean fallback path in GetPdf and DeleteForm |
| P4 | S3 streaming returns readable PDF | ✅ `File(s3Response.ResponseStream, "application/pdf")` |
| P4 | Missing S3 object returns 404 | ✅ Caught in try/catch, returns `NotFound(...)` |
| P4 | `AWSSDK.S3` package (not just `AWSSDK.Core`) | ✅ `<PackageReference Include="AWSSDK.S3" Version="3.*" />` in csproj |
| P5 | Synonyms split trims whitespace | ✅ `StringSplitOptions.TrimEntries` |
| P5 | Dialog saves as comma-separated string | ✅ `MudTextField` bound to `Model.Synonyms` string, passed directly to API |
| P6 | Seed UPSERT (no duplicates) | ✅ SELECT + conditional UPDATE/INSERT |
| P6 | Seed uses env vars for connection string | ✅ `os.environ.get(...)` with fallback defaults |
| P6 | 16 field codes | ✅ Counted: 16 entries in FIELDS list |
| P7 | `IsSensitive` entity default = false | ✅ C# bool defaults to false |
| P7 | `IsSensitive` in API response | ✅ Controller returns full entity |
| P7 | DictionaryController update persists `IsSensitive` | ✅ `field.IsSensitive = body.IsSensitive` in Update |
| P8 | MudAlert below header, above table | ✅ Correct placement in DataDictionary.razor |

---

## Required Action

**Back to Tony — one fix needed:**

**I1 (S3 key parsing):** Resolve the `s3://` prefix handling before S3 is enabled. Pick Option A (strip bucket from URI) or Option B (enforce non-standard `s3://key` convention and comment it). Either is acceptable; just make it unambiguous.

Once that's addressed, this is a **PASS**.

---

_Reviewed by Hawkeye — you see what others miss._

---

## Review Cycle 2 — Targeted Re-Review

**Reviewer:** Hawkeye (Code Reviewer)
**Date:** 2026-02-27
**Commit:** 0ec5ab3
**Scope:** I1 fix verification only — S3 URI key extraction in `FormsController.cs`

### Verdict: ✅ PASS

---

### I1 Fix Verification

**Changed lines (both `GetPdf` and `DeleteForm`):**

```diff
- var key = path.StartsWith("s3://") ? path.Substring(5) : path;
+ var key = path.StartsWith($"s3://{s3Bucket}/") ? path.Substring($"s3://{s3Bucket}/".Length) : path;
```

#### Check 1 — Key extraction correctness ✅
The fix correctly strips the full `s3://{bucket}/` prefix. For a stored path of `s3://my-bucket/forms/abc.pdf`, the extracted key is now `forms/abc.pdf` (correct). Previously it produced `my-bucket/forms/abc.pdf` (wrong — included bucket name in key, causing S3 `NoSuchKey` errors). Fix is accurate.

#### Check 2 — S3 not configured (BucketName empty) ✅
Both methods guard the entire S3 block with `!string.IsNullOrEmpty(s3Bucket) && _s3 != null && IsS3Key(path)`. The `s3Bucket` null/empty check is evaluated first (short-circuit `&&`). No crash when S3 is unconfigured. Unchanged from Cycle 1 — was already correct.

#### Check 3 — Edge case: stored path without trailing slash after bucket ✅ (acceptable)
The prefix pattern is `$"s3://{s3Bucket}/"` — explicitly includes the trailing slash. If a path were malformed as `s3://my-bucket` (no slash, no key), `StartsWith` returns false and the raw path is passed as the key — which would produce an S3 error, not a crash. This is an edge case of corrupt/malformed data, not introduced by this fix, and the behavior is acceptable (error, not exception).

---

### No New Issues Introduced
The change is minimal and surgical. No other code paths were modified. No new risk introduced.

---

### Cycle 2 Summary
- **I1 (S3 key includes bucket name):** ✅ FIXED — correctly resolved in commit `0ec5ab3`
- **Critical issues:** 0
- **New issues introduced:** 0

**Pipeline status: Clear to proceed to Stage 4 (Security Scan).**
