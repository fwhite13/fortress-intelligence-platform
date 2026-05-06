# Review Report — ADO#2824

### Verdict: PASS ✅

**Commit:** `c641dab`
**Reviewer:** Hawkeye (Clint Barton)
**Date:** 2026-05-06
**Cycle:** 1

---

### Spec Compliance Check

**What was asked:** Remove `"bedrock-agent-runtime"` from `ExternalDependencySignals`. Mirror the change in Python validation script. Add targeted G2 recheck script using cached output only.

**Files modified:**
- `src/FortressNexus.Web/Services/WiClassifierService.cs` — ✅ `bedrock-agent-runtime` removed from `ExternalDependencySignals`
- `pipeline/run_v7_validation.py` — ✅ `EXT_DEP_SIGNALS` updated to match
- `pipeline/run_v7_g2_recheck.py` — ✅ new script added, reads cached JSON only

**Spec compliance verdict:** ✅ COMPLIANT

---

### Consistency Audit

**Three-way signal sync (C# ↔ validation.py ↔ g2_recheck.py):**

All three signal arrays contain exactly 11 signals:
```
"rob", "rob nethery", "cloudflare", "cf config", "cf route",
"azure access", "iam request", "iam permissions",
"secrets manager access", "ado pat", "pat token"
```
No `bedrock-agent-runtime` in any of the three. ✅ Three-way sync is clean.

**ExtractExternalOwner dead code:**
Both `WiClassifierService.cs` (line 83) and `run_v7_validation.py`'s `extract_external_owner` retain `bedrock-agent-runtime` in the IAM owner-detection branch. This is now dead code — unreachable unless another ext-dep signal fires first (the method guards on `IsExternalDependency()` first). Architecturally sound. Harmless. ✅

---

### CC Review Summary

CC ran full adversarial review against all 8 check criteria. Zero critical, important, or nitpick findings. Two informational-only notes:

1. `bedrock-agent-runtime` in `ExtractExternalOwner` (C# and Python) is now dead code. Not wrong; can be cleaned up in a future pass if desired.
2. The cached JSON path in `run_v7_g2_recheck.py` is hardcoded as an absolute local path — intentional for a one-shot recheck script.

---

### Issues Found

| Severity | File | Issue |
|----------|------|-------|
| — | — | None |

---

### Specific Checks Verified

1. ✅ `ExternalDependencySignals` no longer contains `"bedrock-agent-runtime"` — exact removal confirmed
2. ✅ All 11 other signals intact — none accidentally dropped
3. ✅ `ExtractExternalOwner` retains `bedrock-agent-runtime` in IAM owner branch (dead but harmless — cannot fire without another ext-dep signal also matching)
4. ✅ `run_v7_validation.py` `EXT_DEP_SIGNALS` matches C# array exactly after change
5. ✅ `run_v7_g2_recheck.py` reads from `ADO2808-BEDROCK-OUTPUT.json` (file confirmed on disk, 134KB) — no boto3, no AWS SDK, no HTTP calls
6. ✅ No other signals changed or removed
7. ✅ Build compiles clean — 0 warnings, 0 errors

### Tony's Residual G2 Failures
2 residual G2 failures are legitimate — ext dep WIs missing `blocked-external`/`owner-*` tags (pre-existing prompt gap). This is a separate concern: the tags are missing from the WIs themselves, not a signal classification issue. Does not block this WI. ✅

---

### Positive Observations

- Clean three-way sync across C#, validation script, and new recheck script
- Recheck script is well-scoped: cache-only, no live API calls, correct AND logic for G2 scoring, exits 1 on failure
- `blocked-external` tag override preserved correctly in recheck script (matches validation.py behavior)
- Commit message and doc comment in recheck script both accurately describe what was changed and why

---

_Hawkeye — eyes on. This one's clean._
