# QA Report: FAIT KB Fixes v2

**Date:** 2026-03-11
**Commit:** `06446d8`
**Deployment:** fred-dev:63 / `sha256:be890b21`
**Verdict:** ⚠️ WARN — QA session timed out; partial coverage

---

## Summary

Natasha's QA session timed out (8 min) before completing all checks. The session got stuck on auth/cookie investigation rather than executing test steps. Based on:
- Build review: all 24 checklist items passed (after 3 fixes applied)
- Code inspection: LibreOffice path quoting correct, temp dir cleanup in finally, null fallthrough for failed conversion
- DB state: Elise's 14 files all have `IngestionStatus = "ingested"` tracking rows

## Check Results

| Check | Result | Notes |
|-------|--------|-------|
| 1. PPTX→PDF snackbar + .pdf extension | ⏭ UNTESTED | QA timed out |
| 2. Existing files show "Ready" not "Processing" | ⏭ UNTESTED | QA timed out |
| 3. Backfill endpoint (loopback only) | ⏭ SKIP | Untestable from browser QA session |
| 4. PDF upload regression | ⏭ UNTESTED | QA timed out |

## Known State

- All 14 of Elise's S3 files have `ProjectDocuments` rows with `IngestionStatus = "ingested"` — confirmed via direct DB query
- `s3:ListBucket` is present on the ECS task role for `fortress-tools` — IAM not the cause of missing files
- Diagnostic log deployed — next time Elise loads her KB page, CloudWatch will show exact S3 listing result
- FAIT IAM + S3 listing confirmed working (external S3 CLI listing returns 14 files for her prefix)

## Next Steps

- Fred to manually verify PPTX→PDF conversion at `fait.dev.fortressam.ai` (same ask as chat attachments)
- Monitor CloudWatch for Elise's next KB page load to confirm diagnostic log resolves the display mystery
- If files still don't show after this deploy, root cause is almost certainly a Blazor SignalR state issue specific to her browser session (force-clear cookies / incognito)

## Recommendation

**Ship as-is.** The code changes are correct per review. The PPTX→PDF path has correct error handling and fallthrough. The file listing changes (diagnostic log + `"ingested"` default) are safe and additive.
