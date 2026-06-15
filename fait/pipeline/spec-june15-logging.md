# Spec: FAIT Harness Logging Improvements — June 15

**Sprint:** FAIT June 15
**Author:** Jarvis
**Date:** 2026-06-15

---

## Background

During S1 QA testing of the June 11 sprint, we confirmed pgvector is connected but could not verify
whether semantic memory queries were actually firing or returning results. Two gaps:

1. **Memory code paths are silent on success.** `searchMemoryChunks()` logs only on error. The
   per-turn autoRecall injection logs when chunks are found but not when 0 results are returned.
   The `read_memory` augmentation path has no success log at all.

2. **No log level infrastructure.** The harness uses ad-hoc `console.log`/`console.warn`/
   `console.debug` with no `LOG_LEVEL` env var control. The pipeline logging standard defines
   `dev → debug`, `prod → info` but this is not implemented. All logs fire at the same level
   regardless of environment.

These two issues are addressed by two separate WIs in this sprint.

---

## WI A: LOG_LEVEL Environment-Aware Logging Infrastructure

### Problem
All `console.log`, `console.warn`, `console.debug` calls fire unconditionally regardless of
environment. The pipeline standard mandates `LOG_LEVEL=dev → debug`, `LOG_LEVEL=prod → info`
but there is no mechanism to enforce it.

### Solution
Introduce a thin logger wrapper at the top of `harness-server.js` that gates output by
`LOG_LEVEL` env var:

```javascript
const LOG_LEVEL = process.env.LOG_LEVEL || 'dev'; // default dev (verbose)
const logger = {
    debug: (...args) => { if (LOG_LEVEL === 'dev') console.log('[DEBUG]', ...args); },
    info:  (...args) => console.log('[INFO]', ...args),
    warn:  (...args) => console.warn('[WARN]', ...args),
    error: (...args) => console.error('[ERROR]', ...args),
};
```

- Replace all existing `console.log` calls used for informational/diagnostic output with
  `logger.info()` or `logger.debug()` based on verbosity:
  - Startup, connection, and schema provisioning logs → `logger.info`
  - Per-request/per-tool trace logs → `logger.debug`
  - Warnings → `logger.warn` (keep `console.warn` behaviour)
  - Errors → `logger.error` (keep `console.error` behaviour)
- The `[pgvector]`, `[harness]`, `[KB]` prefixes already in the logs are preserved — just route
  through logger instead of raw console.
- `console.debug` calls (few exist) → `logger.debug`

### Acceptance Criteria
- AC1: `LOG_LEVEL=prod` env var suppresses all `logger.debug()` calls; `logger.info/warn/error`
  still fire.
- AC2: `LOG_LEVEL=dev` (default) logs everything including debug.
- AC3: Existing `[pgvector]`, `[harness]`, `[KB]` log prefixes preserved in output.
- AC4: No regression — all existing startup/connection logs still visible when `LOG_LEVEL=dev`.
- AC5: `logger` wrapper defined at top of file before first use.

### Test Cases
- T1: Set `LOG_LEVEL=prod`, start harness, confirm no `[DEBUG]` lines in output.
- T2: Set `LOG_LEVEL=dev` (or omit), start harness, confirm debug lines visible.
- T3: Trigger a tool call, confirm `[harness] /turn: toolUse start/complete` still logs.

### goalCondition
`harness-server.js` defines a logger wrapper at the top; all existing console.log/warn/debug calls
replaced with logger equivalents; LOG_LEVEL=prod suppresses debug output per code inspection, or
stop after 20 turns.

---

## WI B: Verbose Memory Logging (uses LOG_LEVEL infrastructure from WI A)

### Problem
Cannot verify whether pgvector queries fire or return results in production logs.
The following code paths have no success logging:

1. `searchMemoryChunks()` — logs only on error; silent on success or 0-results
2. `read_memory` augmentation (line ~1429) — no log on augment run, silent on 0 chunks
3. Per-turn Bedrock autoRecall injection (line ~4329) — logs when chunks found, silent when 0
4. Per-turn CC autoRecall injection (line ~3650) — same pattern as Bedrock

### Solution
Add `logger.debug()` lines at every memory code path decision point:

**`searchMemoryChunks(userId, query, topK, threshold)`:**
```javascript
logger.debug(`[pgvector] search: userId=${userId} query="${query.slice(0,60)}" topK=${topK} threshold=${threshold}`);
// after query:
logger.debug(`[pgvector] search result: userId=${userId} count=${rows.length} query="${query.slice(0,60)}"`);
// in 0-results case (already returned empty array):
// the above result log covers it — count=0 is the signal
```

**`read_memory` augmentation block (~line 1429):**
```javascript
logger.debug(`[pgvector] read_memory augment: userId=${userId} slug="${slug}" pgPool=${!!pgPool} found=${result.found}`);
// after semanticChunks returned:
logger.debug(`[pgvector] read_memory augment result: userId=${userId} slug="${slug}" chunkCount=${semanticChunks?.length ?? 0}`);
```

**Per-turn Bedrock path (~line 4329):**
```javascript
logger.debug(`[pgvector] Bedrock autoRecall: userId=${userId} query="${retrievalQuery.slice(0,60)}"`);
// existing log (injected N chunks) kept — also add when 0:
if (!semanticChunks || semanticChunks.length === 0) {
    logger.debug(`[pgvector] Bedrock autoRecall: userId=${userId} 0 chunks above threshold`);
}
```

**Per-turn CC path (~line 3650):**
Same pattern as Bedrock path above.

### Acceptance Criteria
- AC1: When `searchMemoryChunks` runs, a debug log shows query, userId, topK, threshold.
- AC2: After `searchMemoryChunks` returns, a debug log shows result count (including 0).
- AC3: `read_memory` augmentation logs whether it ran and how many chunks were prepended.
- AC4: Bedrock autoRecall logs when it finds 0 chunks (not just when it finds >0).
- AC5: CC autoRecall logs when it finds 0 chunks (not just when it finds >0).
- AC6: All new logs use `logger.debug()` (suppressed at `LOG_LEVEL=prod`).
- AC7: No regression on existing logging behavior.

### Test Cases
- T1: Send a chat message, check `LOG_LEVEL=dev` harness logs for `[pgvector] search:` and result.
- T2: Call `read_memory` tool, confirm augment log appears with chunk count.
- T3: Send a turn where no matching memory exists, confirm `0 chunks above threshold` log appears.

### goalCondition
All four memory code paths (searchMemoryChunks, read_memory augment, Bedrock autoRecall,
CC autoRecall) emit debug logs at entry and result (including 0-result case); all new logs use
logger.debug(); code inspection confirms, or stop after 20 turns.
