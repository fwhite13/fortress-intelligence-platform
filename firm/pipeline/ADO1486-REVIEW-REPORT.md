# Review Report — ADO#1486
## vpbot: Bot never self-terminates after meeting ends

### Verdict: ✅ PASS

**Reviewer:** Hawkeye (Clint Barton)
**Review Cycle:** 1 of 2
**Date:** 2026-04-01
**CC Model:** Sonnet

---

### Spec Compliance Check

**Three fixes specified:**
1. Leave button disappearance as end-detection signal (2-poll confirmation, 60s grace period)
2. `_monitorInterval` stored on `this` for deterministic cleanup in `stop()`
3. Process-level safety net `setTimeout` with `.unref()` in one-shot mode

**Files in scope:** `src/bot/meeting-bot.ts`, `src/index.ts`
**Files modified:** Same — ✅ no out-of-scope changes, no C# files touched

**Spec compliance verdict:** ✅ COMPLIANT

---

### CC Review Summary

Ran a 24-item adversarial checklist via CC Sonnet. All 24 items passed. CC also ran 5 adversarial analysis probes on edge cases Tony flagged in his build report. All 5 came back safe or benign. Two minor observations noted below (neither blocks shipping).

---

### Consistency Audit

| Cross-reference | Result |
|---|---|
| `_recordingStartTime` set before `_endPollInterval` fires | ✅ Same function, synchronous assignment 73 lines before interval creation — no race |
| `_monitorInterval` cleared in `stop()` and internally in `monitorMeetingStatus()` | ✅ Both clear paths confirmed; `clearInterval(null)` is a Node.js no-op — safe |
| Leave button selector covers both Teams variants | ✅ `[data-tid="hangup-button"], [data-tid="hangup-main"]` confirmed |
| Safety net ms calculation matches `FIRM_MAX_MEETING_HOURS + 30 min` formula | ✅ `(hours * 60 + 30) * 60 * 1000` confirmed |

---

### Critical Issues — 0

None.

---

### Important Issues — 0

None.

---

### Nitpicks — 2

**N1: Safety net uses inline `process.env` re-read instead of module-level constant**
- **File:** `src/index.ts` — line 113
- **Code:** `parseFloat(process.env.FIRM_MAX_MEETING_HOURS || '4')` inside the setTimeout calculation
- **Issue:** The module already parses `FIRM_MAX_MEETING_HOURS` into a constant at line 56. The safety net re-reads the env var inline. Functionally equivalent at startup, but if someone changes the parsing logic for the constant and forgets this inline read, the safety net timer could silently diverge.
- **Fix (optional):** Use the existing parsed constant directly. Not blocking.

**N2: Non-null assertions on `this._monitorInterval!` inside `monitorMeetingStatus()`**
- **File:** `src/bot/meeting-bot.ts` — lines 437, 468, 475
- **Code:** `clearInterval(this._monitorInterval!)`
- **Issue:** TypeScript type is `ReturnType<typeof setInterval> | null`. The `!` assertion tells TypeScript "trust me, not null" at points where `stop()` may have already nulled it. At runtime, `clearInterval(null)` is a Node.js no-op so there's no crash, but the assertion is technically incorrect TypeScript. If Node.js ever changes this behavior, it would be a hidden bug.
- **Fix (optional):** Replace with `if (this._monitorInterval) { clearInterval(this._monitorInterval); this._monitorInterval = null; }` pattern — already used correctly in `stop()`. Not blocking.

---

### Spec Fidelity — All Acceptance Criteria Met

| Criterion | Result |
|---|---|
| `_noLeaveButtonCount: number = 0` field on class | ✅ Line 73 |
| `_recordingStartTime: number = 0` field on class | ✅ Line 74 |
| `_monitorInterval: ReturnType<typeof setInterval> \| null = null` field on class | ✅ Line 72 |
| `_recordingStartTime` set immediately after write-test in `startRecording()` | ✅ Line 237, synchronously before interval setup |
| Leave button check uses correct selectors | ✅ `[data-tid="hangup-button"], [data-tid="hangup-main"]` |
| 60s grace period with correct expression | ✅ `Date.now() - this._recordingStartTime > 60000` |
| 2-poll confirmation threshold is `>= 2` | ✅ Confirmed `>= 2`, counter resets to 0 on button found |
| Leave button check placed AFTER 8 text overlay checks | ✅ Text overlays lines 316–337, leave button lines 342–361 |
| URL-drift detection from #1483 intact and after leave button check | ✅ Lines 374–397 |
| `monitorMeetingStatus()` assigns to `this._monitorInterval` (not local var) | ✅ Line 435 |
| All `clearInterval` calls inside `monitorMeetingStatus()` use `this._monitorInterval` | ✅ 3 calls, all updated |
| `stop()` clears and nulls `_monitorInterval` | ✅ Lines 504–507 |
| `stop()` resets `_noLeaveButtonCount = 0` | ✅ Line 508 |
| `isRecording` guard in `stop()` still intact | ✅ Lines 489–491, throws on double-invoke |
| `_endPollInterval` and `_hardTimeout` still cleared in `stop()` | ✅ Lines 496–503 |
| All 8 text overlay checks from #1483 present | ✅ Lines 317–325 |
| Safety net `setTimeout` in one-shot branch | ✅ Lines 113–118 |
| Safety net uses `FIRM_MAX_MEETING_HOURS + 30 min` | ✅ `(hours * 60 + 30) * 60 * 1000` |
| Safety net has `.unref()` | ✅ Line 118 |
| Safety net calls `process.exit(1)` | ✅ Line 116 |
| `process.once('SIGTERM', ...)` handler untouched | ✅ Lines 173–184 |
| `runOneShotMeeting().then(...exit 0).catch(...exit 1)` intact | ✅ Lines 120–122 |
| No C# files modified | ✅ TypeScript-only WI |
| TypeScript compiles without errors | ✅ Tony confirmed `npm run build` clean |

---

### Adversarial Analysis (Edge Cases)

| Probe | Finding |
|---|---|
| **A1 — Grace period with `_recordingStartTime = 0`** | Safe. `_endPollInterval` is only created inside `startRecording()`, synchronously after `_recordingStartTime` is set. No race condition. |
| **A2 — `_noLeaveButtonCount` not reset if `stop()` throws** | Benign. Bot instances are one-shot. Stale counter never affects a subsequent recording. |
| **A3 — Non-null assertion on `_monitorInterval`** | Safe at Node.js runtime (`clearInterval(null)` is no-op), incorrect TypeScript. Noted as N2. |
| **A4 — Double-clear `_monitorInterval`** | Safe. `clearInterval(null)` is a no-op. Confusing error log possible if internal `stop()` call races external `stop()`, but this is pre-existing behavior unrelated to this WI. |
| **A5 — Safety net calculation** | Correct. Minor inconsistency using inline `process.env` re-read instead of module constant. Noted as N1. |

---

### Positive Observations

- The ordering discipline (text overlays → leave button → URL drift) is correct and deliberate — text detection remains the fast path; leave button detection is the fallback.
- `stop()` cleanup is now deterministic: `_monitorInterval`, `_endPollInterval`, and `_hardTimeout` are all cleared and nulled with null-guards.
- `.unref()` on the safety net timer is the right call — it truly can't prevent normal exit.
- Build report accurately described the implementation with no embellishment. Tony's edge case callouts matched what CC found.
