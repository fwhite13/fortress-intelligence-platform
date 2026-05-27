# Review Report: ADO#4246 — CloudFront Signed URLs (Cycle 2)

## Verdict: PASS ✅

**Review Cycle:** 2 of 2  
**Commit reviewed:** `65699baa` (fixes on top of `2de97a86`)  
**Reviewer:** Clint Barton (code-reviewer)  
**Date:** 2026-05-27

---

## CC Invocation

```bash
cat pipeline/ADO4246-review-brief-cycle2.md | claude --model sonnet --print --dangerously-skip-permissions
```

---

## Cycle 2 Summary

Both Cycle 1 blockers were correctly fixed. No regressions introduced. No new defects found.

---

## Spec Compliance Check

**§7 Acceptance Criteria (re-verified this cycle):**

| # | Criterion | Status |
|---|-----------|--------|
| AC 1 | CloudFront infrastructure documented | ✅ |
| AC 2 | CF signed URLs (not S3 presigned) for preview | ✅ |
| AC 3 | PPTX renders via Office Online iframe | ✅ |
| AC 4 | XLSX renders via Office Online iframe | ✅ |
| AC 5 | PDF no regression | ✅ |
| AC 6 | Key pair stored in Secrets Manager | ✅ |
| AC 7 | Expiry configurable, default 3600 | ✅ |

**Spec compliance verdict: ✅ COMPLIANT**

---

## C1 Fix Verification — PASS

**Issue:** `allow-same-origin` present in Office Online iframe sandbox

**Fixed sandbox string (line 79):**
```html
sandbox="allow-scripts allow-popups allow-forms allow-popups-to-escape-sandbox"
```

- `allow-same-origin` is completely absent from the file ✅
- Only one iframe exists in the component; no other iframes affected ✅
- String matches spec exactly ✅

---

## I1 Fix Verification — PASS

**Issue:** `GetFilePreviewUrlAsync` was dead code; component called CF service directly

**Fix applied (Option A):** Component now routes through `WorkspaceFileSvc.GetFilePreviewUrlAsync`.

| Check | Result |
|-------|--------|
| `@inject ICloudFrontSignedUrlService CloudFrontSvc` removed | ✅ |
| `CloudFrontSvc.` references — zero remaining | ✅ |
| `WorkspaceFileSvc.GetFilePreviewUrlAsync(s3Key, expirySeconds: 3600)` called | ✅ |
| `IWorkspaceFileService` declares `GetFilePreviewUrlAsync` | ✅ |
| `WorkspaceFileService.GetFilePreviewUrlAsync` — CF then S3 fallback logic correct | ✅ |

**URL Detection Heuristic Analysis:**

```csharp
bool isCfUrl = !previewUrl.Contains(".s3.", StringComparison.OrdinalIgnoreCase)
               && !previewUrl.Contains("amazonaws.com", StringComparison.OrdinalIgnoreCase);
```

| Question | Answer |
|----------|--------|
| Can a CF signed URL contain `.s3.` or `amazonaws.com`? | **No** — CF URLs use `*.cloudfront.net`. `AmazonCloudFrontUrlSigner.GetCannedSignedURL` produces `https://<dist>.cloudfront.net/<key>?Expires=...&Signature=...&Key-Pair-Id=...`. No S3 domain anywhere. |
| Can an S3 presigned URL look like a CF URL? | **No** — `GetPreSignedURL` always produces `https://<bucket>.s3.<region>.amazonaws.com/...`. Both `.s3.` and `amazonaws.com` always present. |
| False positive risk from other return paths? | **None** — `GetFilePreviewUrlAsync` has exactly two return paths: CF URL or S3 presigned URL. No third path. |
| Exception handling if method throws? | **Covered** — full `try/catch` wraps the `SelectArtifact` body; sets `_previewError` on any exception. |
| Null-ref risk if `GetSignedUrlAsync` returns null? | **None** — service falls through to `GetPresignedDownloadUrlAsync` which returns non-null. `GetFilePreviewUrlAsync` is `Task<string>` (non-nullable); never returns null. |

---

## Regression Check — PASS

**CF configured and working:** Behavior is equivalent to Cycle 1 code. Same method (`GetCannedSignedURL`) called with same `expirySeconds: 3600`. Same URL produced, same encoding applied. ✅

**CF not configured:** Fallback link still correctly displayed using `rawUrl` (30-min S3 presigned). ✅  
*Note: One minor inefficiency — when CF is not configured, a second call to `GetPresignedDownloadUrlAsync` is made inside `GetFilePreviewUrlAsync` (its result is discarded). `GetPreSignedURL` is synchronous under the hood (`Task.FromResult`), so no I/O cost. Not a defect; minor optimization opportunity for a future pass.*

---

## Full Changeset Re-check — Nothing New Found

- `ICloudFrontSignedUrlService` still registered in `Program.cs` (line 142, singleton) — required by `WorkspaceFileService` constructor ✅
- No other `.razor` files injecting `CloudFrontSvc` ✅
- No secrets introduced in appsettings ✅

---

## Cycle 1 Nitpicks — Status

| # | Issue | Status |
|---|-------|--------|
| N1 | `_cachedPem` not `volatile` | Still present — not blocking |
| N2 | No partial-config warning log | Still present — not blocking |
| N3 | PEM in managed heap | Inherent limitation — not actionable |

These were correctly not addressed by Tony (Nitpick tier). They can be addressed in a follow-up pass or left as accepted risk.

---

## Final Checklist

| Layer | Result |
|-------|--------|
| Spec compliance | ✅ All ACs met |
| C1 fix (sandbox) | ✅ Correct |
| I1 fix (service wiring) | ✅ Correct |
| URL heuristic soundness | ✅ No false positives/negatives possible |
| Null safety | ✅ No null-ref risks |
| Regression: CF on | ✅ Equivalent behavior |
| Regression: CF off | ✅ Equivalent behavior |
| DI registration preserved | ✅ |
| New issues (full re-check) | ✅ None |

---

## Conclusion

Both Cycle 1 blockers are resolved correctly. The implementation is clean, the abstraction boundary is correct (component delegates to service), the URL heuristic is provably sound for the two URL types `GetFilePreviewUrlAsync` can return, and null safety is fully covered. 

**This is ready to ship.**
