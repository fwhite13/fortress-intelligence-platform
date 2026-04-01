# Review Report — ADO#1482
**FIRM vpbot: lobby detection fixes**
**Cycle:** 1 of 2
**Reviewer:** Hawkeye (Clint Barton)
**Date:** 2026-04-01

---

## Verdict: NEEDS-CHANGES

---

## Spec Compliance Check

**§ Files Modified:**
- `src/bot/teams.ts` — ✅ modified as specified
- `src/bot/meeting-bot.ts` — ✅ modified as specified
- No C# files touched ✅

**§ Acceptance Criteria:**

- [x] Uncertain state branch in final join check throws `LobbyTimeoutError` instead of returning — ✅ Verified (`else` branch at lines 515–519 throws correctly)
- [x] `startRecording()` verifies Leave button visible (Teams only) before FFmpeg — ✅ Verified (check is correctly gated, positioned, and wired)
- [x] `waitingRoomPhrases` expanded to 7 entries — ✅ Verified (all 7 entries confirmed including the 5 required phrases)
- [x] `_monitorInterval` class field promotion (bonus) — ✅ Verified

**Spec compliance verdict:** ✅ COMPLIANT on primary criteria — but one issue found in the same block Tony modified.

---

## Consistency Audit

**Cross-file checks:**
- `LobbyTimeoutError` import in `meeting-bot.ts` ↔ export in `teams.ts` — ✅ named import present (line 15)
- `join()` catch block in `meeting-bot.ts` ↔ `LobbyTimeoutError` thrown in `teams.ts` — ✅ catch correctly routes to `failed` + `reason: 'lobby_timeout'`
- `waitingRoomPhrases` array: entry-check uses same array as wait-loop — ✅ no hardcoded bypass found

**`waitForPreJoinScreen()` note:** This function (lines 212–219) has a separate hardcoded 3-phrase array for a different purpose (detecting the pre-join screen). This is NOT a bypass of the lobby wait loop — different code path, different stage. Non-critical.

---

## Issues Found

| Severity | File | Location | Issue | Fix |
|----------|------|----------|-------|-----|
| **Important** | `teams.ts` | Lines 509–511 | `hasEOA` branch falls through — see C1 below | Add throw after screenshot |

---

### C1 — `hasEOA` branch returns normally (Important — blocks PASS)

**File:** `src/bot/teams.ts`, lines 509–511

**Issue:**

```typescript
if (joinedCheck.hasEOA) {
  console.log('[Teams] ❌ ERROR: Hit Classic Teams EOA page! ...');
  await this.screenshot(page, '05-eoa-error');
}   // ← falls through to end of join(), returns normally
```

The `hasEOA` block logs and takes a screenshot — then silently falls through. No throw, no return. `TeamsHandler.join()` returns normally. Back in `MeetingBot.join()`, control reaches `await this.startRecording()`, which spawns FFmpeg and records silence against a dead EOA error page. `reportStatus('failed')` is never called.

**This is in the same state-check block Tony modified for this PR.** The uncertain-state `else` was fixed correctly, but the `hasEOA` branch immediately above it was left in a broken state.

**Severity rationale:** Classic Teams EOA was retired July 1, 2025, making this path less likely in normal flow. However: (a) it sits in the same block that was touched in this PR, (b) the consequence is identical to the bug this PR is fixing (bot records silence without reporting failure), and (c) the fix is trivial.

**Fix:**
```diff
  if (joinedCheck.hasEOA) {
    console.log('[Teams] ❌ ERROR: Hit Classic Teams EOA page! ...');
    await this.screenshot(page, '05-eoa-error');
+   throw new LobbyTimeoutError(); // EOA = not in meeting, treat as lobby timeout
  }
```

(Alternatively, a dedicated `EoaError` class if Tony wants to distinguish this at the `MeetingBot.join()` catch level — but reusing `LobbyTimeoutError` for now is acceptable since the outcome is the same: bot did not get admitted.)

---

## All Checks — CC-Verified Summary

| Check | Item | Result |
|-------|------|--------|
| A | `waitingRoomPhrases` has 7 entries, all required phrases present | ✅ |
| B | Lobby wait entry uses same `waitingRoomPhrases` array, no hardcoded bypass | ✅ |
| C | `!admitted` block throws `LobbyTimeoutError` | ✅ |
| D | `else` (uncertain state) throws `LobbyTimeoutError` | ✅ |
| D† | `hasEOA` branch falls through — returns normally | ❌ ISSUE |
| E | Case-insensitive phrase matching at both comparison sites | ✅ |
| F | All 5 class fields present (`_endPollInterval`, `_hardTimeout`, `_noLeaveButtonCount`, `_recordingStartTime`, `_monitorInterval`) | ✅ |
| G | Admission check: after write-test, before `_recordingStartTime` | ✅ |
| H | Admission check gated on `platform === 'teams'` | ✅ |
| I | `getByRole('button', { name: /Leave/i })` with 3s timeout | ✅ |
| J | Not-visible path: `reportStatus('failed')` + descriptive error + early return | ✅ |
| K | Entire check wrapped in try/catch, catch is non-fatal (warn + continue) | ✅ |
| L | `isRecording` guard in `stop()` still intact | ✅ |
| M | `_monitorInterval` cleared + nulled in `stop()`, `_noLeaveButtonCount` reset | ✅ |
| N | No C# files modified | ✅ |
| O | `LobbyTimeoutError` import present in `meeting-bot.ts` | ✅ |
| P | `join()` catches `LobbyTimeoutError`, sends `failed` + `reason: 'lobby_timeout'` | ✅ |

---

## Positive Observations

- The pre-recording admission check is cleanly implemented: platform gate, regex button selector, proper try/catch, correct ordering relative to write-test and `_recordingStartTime`.
- `_monitorInterval` promotion is a clean one-liner that eliminates a real interval leak.
- The `join()` catch routing is exactly correct — `LobbyTimeoutError` → failed callback, other errors re-thrown.
- Case-insensitive matching is applied consistently at both comparison sites.

---

## What Tony Needs to Fix

**One change required:**

In `src/bot/teams.ts`, add a throw after the `hasEOA` screenshot (lines 509–511):

```typescript
if (joinedCheck.hasEOA) {
  console.log('[Teams] ❌ ERROR: Hit Classic Teams EOA page! ...');
  await this.screenshot(page, '05-eoa-error');
  throw new LobbyTimeoutError();  // ← ADD THIS
}
```

That's it. Everything else is correct and clean. Resubmit for cycle 2.

---

_Reviewed by Hawkeye — Cycle 1 of 2_
