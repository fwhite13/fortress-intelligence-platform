# QA Report — ADO #992: Task Visibility Fix (AssignedToUserId)

**Date:** 2026-03-21  
**QA Analyst:** Black Widow (Natasha Romanoff)  
**Environment:** https://famos.dev.fortressam.ai  
**Commit:** `2094c2e`  
**Verdict:** ⚠️ PARTIAL PASS

---

## Summary

T1 (health) confirmed. Code fix verified at source. ECS deployment confirmed correct image. T2-T6 (browser UI tests) blocked by Entra session expiry — headless browser was redirected to Microsoft MFA auth wall, which cannot be completed without interactive MFA. **Fred's manual sign-off required to complete T2-T6.**

---

## Test Results

### T1 — Health Check
- **Result:** ✅ PASS
- `curl -sk -o /dev/null -w "%{http_code}\n" https://famos.dev.fortressam.ai/health` → `200`

### T2 — Task Center Loads
- **Result:** ⚠️ BLOCKED (auth wall)
- Browser navigated to `/tasks` → redirected to `login.microsoftonline.com` (Entra MFA required).
- Headless browser session expired after earlier tests today.
- Cannot complete without interactive Entra MFA.

### T3 — Account-Linked Task
- **Result:** ⚠️ BLOCKED (auth wall)
- Requires authenticated session.

### T4 — General Task
- **Result:** ⚠️ BLOCKED (auth wall)
- Requires authenticated session.

### T5 — Filter Chips
- **Result:** ⚠️ BLOCKED (auth wall)
- Requires authenticated session.

### T6 — No Crash / Blazor Errors
- **Result:** ⚠️ BLOCKED (auth wall)
- Requires authenticated session.

---

## Code Fix Verification

### Commit `2094c2e` Diff — `TaskService.cs`

Three query sites in `TaskService.cs` were updated (lines 28, 96, 127):

**Before:**
```csharp
(t.OpportunityId != null && t.Opportunity.OwnerUserId != null && t.Opportunity.OwnerUserId == userId && !t.Opportunity.IsClosed)
```

**After:**
```csharp
(t.OpportunityId != null && !t.Opportunity!.IsClosed && (t.Opportunity.OwnerUserId == userId || t.AssignedToUserId == userId))
```

✅ Fix is correct and complete:
- Added `|| t.AssignedToUserId == userId` — previously invisible tasks now visible
- Removed redundant `t.Opportunity.OwnerUserId != null` guard (null-coalescing `!` handles this)
- All 3 query sites updated: `GetTasksForUserAsync`, `CountTasksForUserAsync`, and the summary count query

### ECS Deployment Verification

| Check | Result |
|-------|--------|
| ECS service status | ACTIVE, 1/1 running |
| Task definition | `famos-dev:4` (PRIMARY, steady state) |
| Running image digest | `sha256:da64bc2420b6...` |
| ECR `latest` digest | `sha256:da64bc2420b6...` ✅ Match |
| Image pushed at | 2026-03-21 08:30:49 EST |
| Task started at | 2026-03-21 08:33:49 EST |
| Commit timestamp | 2026-03-21 08:28:09 EST |

Timeline is consistent: commit → image push → task restart, all within 6 minutes. Correct image is running.

---

## Auth Issue Note

The `openclaw` browser profile had a valid Entra session earlier today (used for ADO#986 QA at ~04:10 AM EST). That session expired before this QA run (~08:36 AM EST). The FIP platform uses `famos.dev.fortressam.ai` → Entra OIDC → `.FortressAI.Session` cookie shared across `.dev.fortressam.ai`. Browser automation cannot complete the MFA challenge.

---

## ADO Comment

Posted to ADO #992 — comment ID `727530`.

---

## Verdict

**PARTIAL PASS** — Code fix is verified correct at source. Deployment is confirmed with right image/digest. Health endpoint passing. Browser tests T2-T6 require Fred to manually verify via authenticated session.

**Actions required from Fred:**
1. Log into https://famos.dev.fortressam.ai
2. Navigate to `/tasks` — confirm task center loads (T2)
3. Add an account-linked task on an opportunity you don't own — confirm it appears (T3)
4. Add a general task — confirm it appears in "General Tasks" section (T4)
5. Test filter chips (T5)
6. Navigate around, confirm no Blazor errors (T6)

---

*Report by Natasha Romanoff (qa-analyst) — 2026-03-21*
