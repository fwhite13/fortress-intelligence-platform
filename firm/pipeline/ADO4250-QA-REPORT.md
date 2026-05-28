# QA Report: ADO#4250 — FIRM: AzureAd__ClientId Swap

**Date:** 2026-05-27  
**Tester:** Black Widow (QA Analyst)  
**Task Def:** `firm-web:133`  
**Verdict:** ⚠️ PASS (pending human gate)

---

## Tests Run

### Step 1 — Task Definition Environment Variable ✅ PASS
- **Running task def:** `arn:aws:ecs:us-east-1:742932328420:task-definition/firm-web:133`
- **AzureAd__ClientId:** `eda4d502-8c93-422e-b7fb-bb922a2a472e` ✅ (matches expected)
- **Old value (`a2de171d-...`) NOT present** ✅
- ECS service healthy, 1/1 running (`:132` fully drained per deployment report)

### Step 2 — CloudWatch Error Check ✅ PASS
- Checked `/ecs/firm-web` for `ERROR` events in the past 15 minutes
- **Result: No errors found**
- No auth failures, no startup errors, no Graph token issues logged

### Step 3 — Human Gate ⚠️ PENDING
- **Required:** Fred must re-authenticate in FIRM (first login after client swap will prompt re-auth — expected behavior per WI)
- **Required:** Fred confirms meeting list populates from Outlook calendar
- **QA cannot complete this step** — requires real Microsoft/Entra credentials and user interaction
- This is **expected and documented behavior** per WI description: existing refresh tokens were issued to the old client and will fail once; new tokens issued to `eda4d502` will work normally after re-auth

---

## Summary

| Test | Result | Notes |
|------|--------|-------|
| Task def running is `firm-web:133` | ✅ PASS | Confirmed via ECS describe-services |
| `AzureAd__ClientId` = `eda4d502-8c93-422e-b7fb-bb922a2a472e` | ✅ PASS | Exact match confirmed |
| No startup errors / auth errors in CloudWatch (last 15 min) | ✅ PASS | Zero ERROR events |
| Fred re-auth + calendar populates | ⚠️ HUMAN GATE | Fred must confirm manually |

---

## Verdict

**PASS (human gate: Fred re-auth + calendar confirm)**

All automated acceptance criteria satisfied. The config-only deployment is verified:
- Correct task definition running
- Correct `AzureAd__ClientId` in place
- No post-deploy errors logged

Full acceptance requires Fred to log into FIRM, complete the re-auth prompt, and confirm calendar data loads. Do not mark WI Done until Fred confirms.

---

_QA by Black Widow — Trust nothing. Verify everything._
