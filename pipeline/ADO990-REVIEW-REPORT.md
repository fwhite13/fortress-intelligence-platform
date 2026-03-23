# Review Report: ADO#990 — Auto-Memory Plugin
**Reviewer:** Hawkeye (Clint Barton)  
**Commit:** `55619c7`  
**Date:** 2026-03-21  
**Verdict:** NEEDS-CHANGES  
**Cycle:** 1 of 2  
**Review tool:** Claude Code CLI (`claude --permission-mode bypassPermissions --print`)

---

## Checklist Results

### `index.ts` — before_prompt_build hook

| # | Item | Result |
|---|------|--------|
| 1 | Hook registered via `api.on('before_prompt_build', ...)` | ✅ Line 932 |
| 2 | Top-level try/catch — returns `undefined` on error, never re-throws | ✅ Lines 939–990 |
| 3 | Embeds inbound message via HTTP call (non-blocking) | ✅ GET /search with 5s timeout |
| 4 | Searches pgvector and returns `prependSystemContext` on hit | ✅ Line 974 |
| 5 | Respects `autoRecall: false` — skips if disabled | ✅ `if (autoRecall)` guard line 931 |
| 6 | Respects `recallMinScore` threshold | ✅ Passed to `formatAutoRecallContext()` |
| 7 | Respects `recallLimit` | ✅ Passed to `/search` as `limit` param |

### `index.ts` — agent_end hook

| # | Item | Result |
|---|------|--------|
| 8 | Hook registered via `api.on('agent_end', ...)` | ✅ Line 1175 |
| 9 | Outer handler is synchronous — no `async` on handler itself | ❌ **Line 1175: `async (event, ctx) =>`** |
| 10 | All async work wrapped in `setImmediate(async () => { ... })` | ✅ Line 1182 |
| 11 | Respects `autoCapture: false` | ✅ `if (autoCapture)` guard line 1174 |

### `index.ts` — runFactExtractionPipeline

| # | Item | Result |
|---|------|--------|
| 12 | Defined as `const` closure INSIDE `register(api)` — NOT at module level | ✅ Line 1005, inside `register()` |
| 13 | Uses Pepper URL `http://100.118.68.63:11434` | ✅ Via `pepperOllamaUrl` default (line 38) |
| 14 | Stage 1: calls extraction model, produces facts | ✅ `extractFacts()` line 1013–1018 |
| 15 | Stage 2: dedup model for INSERT/UPDATE/DISCARD decisions | ✅ `dedupFacts()` line 1064–1069 |
| 16 | Results written via `POST /ingest/update` for UPDATE decisions | ✅ Lines 1143–1154 |

### JSONL retry queue

| # | Item | Result |
|---|------|--------|
| 17 | Uses `fs.appendFile` (O_APPEND atomic) | ✅ Line 429, `appendFile` via `import("node:fs/promises")` |
| 18 | `chunk_type: "fact"` used for extracted facts | ✅ Lines 1127 (INSERT), 1150 (UPDATE) |

### `openclaw.plugin.json`

| # | Item | Result |
|---|------|--------|
| 19 | `autoRecall`, `autoCapture`, `recallLimit`, `recallMinScore` in configSchema + uiHints | ✅ All four present |
| 20 | Fact extraction config fields present: `pepperOllamaUrl`, `extractionModel`, `dedupModel`, `factExtractionEnabled`, `retryQueuePath` | ✅ All five present in both sections |

### `serve.py`

| # | Item | Result |
|---|------|--------|
| 21 | `POST /ingest` validates content + chunk_type BEFORE embed() | ✅ Validation lines 275–282, embed() line 286 |
| 22 | `POST /ingest/update` — atomic delete-old + insert-new | ✅ Lines 338–415 |
| 23 | 5-minute dedup window on `/ingest` | ✅ Lines 296–302, `INTERVAL '5 minutes'` |
| 24 | `chunk_id` back-filled into metadata | ✅ Lines 323–329 (in `/ingest`) |

---

## Findings

### 🔴 CRITICAL — score/distance field mismatch breaks auto-recall entirely

**File:** `index.ts` (line 66) + `serve.py` (line 178)

`serve.py`'s `/search` endpoint returns `score` (a 0–1 value = `1 - cosine_distance`):
```python
"score": float(r["score"]),   # serve.py line 178
```

But `RagSearchResult` in `index.ts` declares `distance: number` (line 66), and ALL filtering uses `r.distance`:
- `formatResults()` line 91: `distanceToScore(r.distance)`
- `formatAutoRecallContext()` line 146: `distanceToScore(r.distance)`
- `fetchExistingFacts()` line 384: `distanceToScore(r.distance)`

`distanceToScore()` expects a 0–2 range distance value. But the API sends `score` (0–1). In JavaScript, `r.distance` will be `undefined`. `distanceToScore(undefined)` → `NaN`. All `>= recallMinScore` comparisons fail → **zero results ever injected, auto-recall silently does nothing**.

**Fix (recommended):** Change `RagSearchResult` to `score: number`, update all three call sites to use `r.score` directly (already 0–1, no conversion needed), remove `distanceToScore()` or mark it unused.

**Alternative:** Change `serve.py` `/search` to return `distance` instead of `score` (requires re-verifying the SQL math).

---

### 🟠 HIGH — agent_end handler is async (spec violation)

**File:** `index.ts` line 1175

```typescript
api.on("agent_end", async (event: any, ctx: any) => {  // ← async is wrong
```

Spec §8 review priorities: *"agent_end hook fires synchronously; setImmediate() defers the actual work. Verify that the outer handler returns immediately."*

An `async` handler returns a Promise, not `undefined`. If the framework awaits the returned Promise, the `setImmediate` wrapper is meaningless — the framework holds a reference and the turn is not released until the async chain resolves (which includes the entire `setImmediate` body).

**Fix:** Remove `async` from the outer handler:
```typescript
api.on("agent_end", (event: any, ctx: any) => {   // synchronous
```
All the async work already lives inside `setImmediate(async () => { ... })`.

---

### 🟡 MEDIUM — `/ingest/update` missing chunk_id back-fill

**File:** `serve.py` lines 394–411

`/ingest` back-fills `chunk_id` into metadata after INSERT (lines 323–329), enabling `fetchExistingFacts()` to retrieve chunks by their DB id via `r.metadata?.chunk_id`. But `/ingest/update` (lines 394–411) does not do the same after its INSERT.

New facts inserted via UPDATE will have `chunk_id` missing from metadata → future dedup passes won't be able to reference them by ID.

**Fix:** After the `INSERT RETURNING` in `/ingest/update`, add:
```python
new_id = row[0] if row else None
# Back-fill chunk_id into metadata
if new_id is not None:
    metadata["chunk_id"] = new_id
    cur.execute(
        "UPDATE chunks SET metadata = %s WHERE id = %s",
        (json.dumps(metadata), new_id)
    )
conn.commit()
```
(Same pattern as `/ingest` lines 323–329.)

---

## Summary

22 of 24 checklist items pass. Three findings:

| Severity | Finding | File |
|----------|---------|------|
| 🔴 CRITICAL | `score`/`distance` field mismatch — auto-recall returns zero results | `index.ts` + `serve.py` |
| 🟠 HIGH | `agent_end` outer handler is `async` — defeats `setImmediate` non-blocking guarantee | `index.ts` line 1175 |
| 🟡 MEDIUM | `/ingest/update` missing `chunk_id` back-fill in metadata | `serve.py` lines 394–411 |

**Verdict: NEEDS-CHANGES.** Fix all three before re-review. No scope creep — these are the only changes required.

---

*Hawkeye out.*
