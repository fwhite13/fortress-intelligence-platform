# Review Report: WI909 — FIRM v1 Bug Fixes

**Reviewer:** Hawkeye (Clint Barton)  
**Commit:** `dff2e61` (WI#844, March 17)  
**Review Date:** 2026-03-20  
**Review Cycle:** 1 of 2  
**Verdict:** ✅ **PASS**

---

## CC CLI Invocation

```bash
cat /tmp/wi909-review-brief.md | claude --model sonnet -p
```

---

## Files Reviewed

| File | Change |
|------|--------|
| `Models/FirmMeetingKbPush.cs` | New — KB push tracking model |
| `Services/MeetingService.cs` | FaitUserId resolution at login |
| `Controllers/MeetingsApiController.cs` | Audio redirect + push-to-kb + kb-status endpoints |
| `Components/Pages/MeetingDetail.razor` | HttpClient fix + multi-KB UI |
| `Services/FirmKbService.cs` | PushDocumentAsync + GetPushedScopesAsync |
| `Data/FirmDbContext.cs` | FirmMeetingKbPush entity + EF mapping |
| `Data/DatabaseInitializationService.cs` | firm_meeting_kb_pushes schema |

---

## Acceptance Criteria Verification

| # | Priority | Item | Result |
|---|----------|------|--------|
| H1 | HIGH | FaitUserId guard in `MeetingService.cs` — `if (string.IsNullOrEmpty(user.FaitUserId))` | ✅ PASS |
| H2 | HIGH | Dedup before S3 in `PushDocumentAsync` — `FirstOrDefaultAsync(...)` check before `PutObjectAsync` | ✅ PASS |
| H3 | HIGH | Audio returns `Redirect(url)` — NOT `Ok(new { url })` | ✅ PASS |
| M4 | MEDIUM | MeetingDetail uses `@inject HttpClient Http`; both push methods use `Http.PostAsJsonAsync(...)` | ✅ PASS |
| M5 | MEDIUM | `push-transcript-to-kb` and `push-summary-to-kb` preserved with `[Obsolete]` and full implementation bodies | ✅ PASS |
| M6 | MEDIUM | `ResolveFaitUserIdAsync` fully non-fatal — `try/catch`, `LogWarning`, never throws | ✅ PASS |
| L7 | LOW | `CREATE TABLE IF NOT EXISTS firm_meeting_kb_pushes` in `DatabaseInitializationService.cs` | ✅ PASS |
| L8 | LOW | All snake_case columns on `FirmMeetingKbPush` entity mapped via `HasColumnName()` | ✅ PASS |

---

## Consistency Audit

| Check | Result |
|-------|--------|
| `FirmMeetingKbPush` entity — all snake_case columns mapped via `HasColumnName()` | ✅ |
| New `/push-to-kb` routes to `PushDocumentAsync`; old endpoints route to legacy methods — no cross-wiring | ✅ |
| `FaitUserId` resolution guard — only fires when null/empty | ✅ |
| Dedup check uses `FirmMeetingKbPushes` table (not old boolean flags) for new code path | ✅ |
| `CREATE TABLE IF NOT EXISTS` — idempotent, safe on restart | ✅ |

---

## Critical Issues

None.

---

## Important Issues

None.

---

## Nitpicks (non-blocking)

**N1 — `GetCurrentUserId()` is dead code** (`MeetingsApiController.cs`)  
The stub returns `null` but is never called — all routes use `ResolveOwnedMeetingWithUser` which correctly resolves the user via EntraOid. Safe to delete in a cleanup pass. Not a bug, not a blocker.

---

## Positive Observations

- **Dedup-before-S3 ordering** is correct and intentional — the inline comment makes the intent explicit.
- **`ResolveFaitUserIdAsync` failure** is fully swallowed with a log warning; no exception can propagate to the caller.
- **Audio endpoint** returns `Redirect(url)` correctly — avoids exposing the presigned URL in the response body.
- **Legacy endpoints preserved** with `[Obsolete]` attribution and full implementation bodies — not stubs.
- **`PushDocumentAsync`** builds document content once and iterates scopes — no redundant DB reads.

---

## Known Trade-off (Accepted)

Legacy `PushTranscriptAsync`/`PushSummaryAsync` in `FirmKbService` still use old boolean flags and bypass the `FirmMeetingKbPushes` dedup table. This is acceptable: those paths are only reachable via the `[Obsolete]`-marked endpoints, which are deprecated in favour of `/push-to-kb`. No new code calls them.

---

## Summary

All 8 review priorities from spec Part 7 are correctly implemented. No blocking issues. One dead-code nitpick noted for a future cleanup pass.

**Pipeline proceeds to Stage 4 (Security).**
