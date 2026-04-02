# QA Report — ADO#1487 Cycle 2

**Analyst:** Natasha Romanoff (Black Widow)  
**Date:** 2026-04-01  
**Deployment:** firm-web:76 — `[AllowAnonymous]` on VpCallback  
**Commit:** `8342d8e`

---

## Verdict: ✅ PASS (5/5)

---

## Test Results

| TC | Description | Result | Details |
|----|-------------|--------|---------|
| TC1 | firm-web:76 healthy | ✅ PASS | taskDef=`:76`, rolloutState=`COMPLETED`, running=`1` |
| TC2 | `[AllowAnonymous]` present in source | ✅ PASS | Attribute present immediately after `[HttpPost("/api/vp/callback")]` |
| TC3 | `X-Bot-Secret` validation intact | ✅ PASS | `expectedSecret` / `X-Bot-Secret` header check present, fail-closed |
| TC4 | FipShared regression | ✅ PASS | HTTP 302 (auth redirect, not 404) — asset route live |
| TC5 | TG healthy | ✅ PASS | 1 healthy target (`172.31.38.214`), 1 draining (old task cycling out — expected) |

---

## Detail Notes

**TC2 — Source confirmation:**
```
[HttpPost("/api/vp/callback")]
[AllowAnonymous]
public async Task<IActionResult> VpCallback([FromBody] VpCallbackPayload payload)
```
`[AllowAnonymous]` is placed directly after the route attribute and before the method signature. ✅

**TC3 — Secret validation:**
```csharp
var expectedSecret = _config["Firm:BotCallbackSecret"];
var providedSecret = Request.Headers["X-Bot-Secret"].FirstOrDefault();
if (string.IsNullOrEmpty(expectedSecret) || providedSecret != expectedSecret)
```
Fail-closed: missing config blocks all requests. Validation intact. ✅

**TC5 — TG state:**
- `172.31.38.214` → `healthy` (new task, :76)
- `172.31.73.3` → `draining` (old task cycling out — normal ECS rolling deploy behavior)

---

## Summary

firm-web:76 is live, healthy, and correctly configured. `[AllowAnonymous]` is confirmed on `VpCallback` with `X-Bot-Secret` header validation intact (fail-closed). No regressions detected. Ready for Fred's live callback test.

---

_Trust nothing. Verify everything._
