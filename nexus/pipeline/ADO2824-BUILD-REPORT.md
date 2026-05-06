# Build Report — ADO#2824

## fix(ADO#2824): remove bedrock-agent-runtime as standalone ExternalDependencySignal

**Date:** 2026-05-06  
**Commit:** `c641dab`  
**Branch:** main  
**WI:** ADO#2824 | Feature #2795 | Epic #2793

---

## What was built

Removed `"bedrock-agent-runtime"` from `ExternalDependencySignals` in `WiClassifierService.cs` (and the mirrored Python copy in `run_v7_validation.py`). This signal was causing 14 false positives in G2 validation — internal implementation WIs (IAM task roles, bedrock SDK integrations, migration stories) were being flagged as external dependencies.

---

## Files changed

- `src/FortressNexus.Web/Services/WiClassifierService.cs` — Removed `"bedrock-agent-runtime"` from `ExternalDependencySignals` array (final entry in list)
- `pipeline/run_v7_validation.py` — Removed `"bedrock-agent-runtime"` from Python-mirrored `EXT_DEP_SIGNALS` list (keeps validator in sync with C# service)
- `pipeline/run_v7_g2_recheck.py` — New: targeted G2 re-check script against cached ADO2808-BEDROCK-OUTPUT.json (no Bedrock call needed)

---

## Parallelization used

No — single-file change, sequential.

---

## CC sessions run

0 — Trivial one-liner removal done directly (no CC needed per AGENTS.md guidance).

---

## Validation G2 result

**Before fix:** 18 ext dep WIs detected (14 false positives via `bedrock-agent-runtime` signal)  
**After fix:** 4 ext dep WIs detected (all legitimate CloudFlare/CORS/KB-confirm WIs)  
**False positives eliminated:** 14

G2 scoring against cached ADO2808-BEDROCK-OUTPUT.json:
- 6 total WIs with `isExternalDependency=true` (after tag override also applied)
- **2 still failing G2** — missing `blocked-external`/`owner-*` tags
  - `As a developer, I want CORS configuration implemented...` — tagged `spec-section-2` only
  - `Test end-to-end routing from CloudFlare to fip-mcp service` — tagged `auto-generated, needs-review` only
- These failures are **pre-existing prompt gaps** (not caused by this fix) — the prompt doesn't instruct the model to tag these correctly
- ADO#2824's specific false positive (`bedrock-agent-runtime` triggering on internal WIs) is **fully resolved**

> **G2 status (for ADO#2824 fix scope):** FALSE POSITIVE eliminated ✓  
> Residual G2 failure is a separate prompt issue requiring a follow-on WI.

---

## Acceptance criteria verification

- [x] `"bedrock-agent-runtime"` removed from `ExternalDependencySignals` — confirmed in C# file
- [x] Python mirror updated in `run_v7_validation.py` — confirmed
- [x] `ExtractExternalOwner` logic unchanged — verified (harmless when unreachable)
- [x] Commit message matches spec — `fix(ADO#2824): remove bedrock-agent-runtime as standalone ExternalDependencySignal`
- [x] 14 false positive WIs no longer classified as external deps — verified via `run_v7_g2_recheck.py`

---

## Known edge cases / things Clint should scrutinize

1. **`ExtractExternalOwner` has `"bedrock-agent-runtime"` in its IAM branch** — left intentionally as per spec (harmless: only reached after `IsExternalDependency` returns true, which now requires a true IAM/external signal)
2. **G2 residual failure** — 2 legitimate ext dep WIs still missing tags. This is a prompt fix needed in a follow-on WI, not in scope here.
3. **`run_v7_validation.py` Python mirror** — The script maintains its own copy of the classifier signals. Any future changes to C# signals must also be mirrored here.

---

## How to test locally

```bash
python3 /home/fredw/projects/fip/nexus/pipeline/run_v7_g2_recheck.py
# Expect: 4 ext dep WIs (no bedrock-agent-runtime WIs in list), G2 FAIL only for 2 tag-missing WIs
```

---

_Build Report filed. Sending to Clint for review._
