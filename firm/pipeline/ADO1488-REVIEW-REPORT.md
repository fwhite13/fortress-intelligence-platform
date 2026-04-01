# Review Report — ADO#1488

**Task:** Pre-bake Whisper large-v3 into Docker image (no runtime HF Hub download)
**File reviewed:** `Dockerfile` in `firm-vpbot/`
**Commit:** `4a9b780`
**Reviewer:** Hawkeye (Clint Barton)
**Date:** 2026-04-01

---

### Verdict: ✅ PASS

---

## CC Review Summary

Claude Code read the full Dockerfile and `src/transcribe/transcribe.ts`. All 7 acceptance criteria passed. No false positives — findings were clear-cut. Two minor observations noted below (neither blocking).

---

## Spec Compliance Check

**§ What changed:** 5 lines added to Stage 2 only.

| Check | Result |
|-------|--------|
| 1. `ENV HF_HOME` set before pre-bake `RUN` | ✅ PASS — line 72 (ENV) precedes line 73 (RUN) |
| 2. Pre-bake is in Stage 2 only | ✅ PASS — Stage 2 begins at line 20 (`FROM ubuntu:24.04`); pre-bake at lines 72–73 |
| 3. Pre-bake after `pip3 install faster-whisper` | ✅ PASS — pip install at line 69, pre-bake at line 73 |
| 4. Pre-bake before `COPY --from=builder` | ✅ PASS — pre-bake at line 73, first COPY at line 81 |
| 5. `HF_HOME` is persistent `ENV` (not `ARG`) | ✅ PASS — `ENV` instruction confirmed |
| 6. No other changes | ✅ PASS — only lines 71–74 added (comment + ENV + RUN + echo) |
| 7. Model params match `transcribe.ts` | ✅ PASS — `large-v3` / `cpu` / `int8` match exactly |

---

## Consistency Audit

**Dockerfile pre-bake (line 73):**
```python
WhisperModel('large-v3', device='cpu', compute_type='int8')
```

**`transcribe.ts` runtime (line 37):**
```python
WhisperModel(model_size, device="cpu", compute_type="int8")
```
`model_size` defaults to `"large-v3"` (line 34) and constructor fallback (line 64). **Exact match.**

---

## Issues Found

None. No critical, important, or nitpick issues.

---

## Observations (Non-Blocking)

### O1: `WHISPER_MODEL` env override will bust the cache at runtime
`transcribe.ts:64` reads `process.env.WHISPER_MODEL || 'large-v3'`. If that env var is set in the Fargate task definition to a different model, faster-whisper will attempt a runtime HF Hub download — which will fail in a VPC-locked environment with no egress to huggingface.co.

**Not a bug in this PR.** Worth documenting in the task definition or `README` that `WHISPER_MODEL` must stay unset (or match what's pre-baked) in Fargate.

### O2: Cache dir owned by root
Pre-bake runs as root; runtime process runs as root. No permission mismatch today. If a non-root `USER` is ever introduced, the `/app/.cache/huggingface` directory ownership will need to be adjusted. Flag if hardening is planned.

---

## Layer Ordering (Verified Correct)

```
[69] RUN pip3 install faster-whisper          ← package installed
[72] ENV HF_HOME=/app/.cache/huggingface      ← env set
[73] RUN python3 -c "WhisperModel(...)"       ← model downloaded into image layer
[81] COPY --from=builder ...                  ← app code copied (separate layer)
```

Correct. Model cache is isolated in its own Docker layer, cached independently of app code changes.

---

## Positive Observations

- Layer ordering is intentional and correct — model cache won't be invalidated by app code changes.
- `ENV` (not `ARG`) ensures `HF_HOME` persists to runtime. faster-whisper will find the baked model.
- Echo confirmation line is a nice build-time signal: `"Whisper large-v3 pre-baked successfully"`.

---

_Cycles: 1 | No follow-up required_
