# QA Report: ADO#2887 — FAIT v2 FORGE KB Integration Service

**Verdict: ⚠️ PARTIAL PASS**

> Post-auth flow requires Fred's manual sign-off before marking Done.

---

## Environment

- **Target URL:** `https://fait-v2.dev.fortressam.ai`
- **Task Definition:** `fait-v2:5` (image `555b283`)
- **ECS Task (running):** `632fe54b13bb4bc1b6738ae048ce7fb3`
- **Test Date:** 2026-05-07 ~11:18 EDT
- **Tester:** Natasha Romanoff — QA Analyst

---

## Test Results

| TC | Test | Result | Details |
|----|------|--------|---------|
| TC1 | Health endpoint `/health` | ✅ PASS | HTTP 200, body: `OK`, 124ms via ALB |
| TC2 | Service reachable at root | ✅ PASS | HTTP 302 → redirect to login (not 502/503) |
| TC3 | Auth redirect destination | ⚠️ PARTIAL | Redirects to `/auth/redirect-to-login` correctly; final hop to `fip.dev.fortressam.ai` not verifiable from test host (DNS not resolvable externally — see notes) |
| TC4 | `mcp_servers` forge-kb DB row | ✅ PASS | Row confirmed: `name='forge-kb'`, `endpoint_url='https://api.fortressam.ai/mcp'`, `is_active=1` |
| TC5 | ECS service health | ✅ PASS | `running=1`, `desired=1`, task def `fait-v2:5` ✅ |
| TC6 | No critical errors in logs | ✅ PASS | Current task (`632fe54b`) log is clean: only forge-kb seed `[INF]` + expected `HTTP_PORTS` `[WRN]`. MySQL errors were from earlier crashed tasks (pre-deploy). |

**Score: 5/6 full passes, 1 partial (TC3 — auth redirect final hop)**

---

## TC Detail Notes

### TC1 — Health Check
Tested via ALB with `Host: fait-v2.dev.fortressam.ai` header (DNS not resolvable from test host):
```
curl -sk -H "Host: fait-v2.dev.fortressam.ai" \
  https://fortress-tools-alb-487057611.us-east-1.elb.amazonaws.com/health
→ HTTP 200, body: OK, 124ms
```

### TC2 — Service Reachability
Root path returns 302 redirect (correct — no valid session cookie):
```
HTTP/2 302
location: http://fait-v2.dev.fortressam.ai/auth/redirect-to-login?ReturnUrl=%2F
```

### TC3 — Auth Redirect
The redirect chain from the browser's perspective:
1. `https://fait-v2.dev.fortressam.ai/` → 302 → `http://fait-v2.dev.fortressam.ai/auth/redirect-to-login?ReturnUrl=%2F`
2. `http://` → 301 (ALB port-80 rule) → `https://fait-v2.dev.fortressam.ai/auth/redirect-to-login?ReturnUrl=%2F`
3. `https://fait-v2.dev.fortressam.ai/auth/redirect-to-login?ReturnUrl=%2F` → **should** → `https://fip.dev.fortressam.ai/...`

Step 3 could not be verified: `fait-v2.dev.fortressam.ai` does not resolve via external DNS from the test host (WSL2 or browser host). The intermediate `http://` redirect in step 1 is a known ASP.NET Kestrel/ALB forwarded-headers behavior — the ALB port-80 listener handles it cleanly. 

**⚠️ NOTE:** The app generates `http://` Location headers instead of `https://` on the first redirect. This works in practice because of the ALB HTTP→HTTPS 301 rule, but is mildly concerning — if `UseForwardedHeaders` is fully configured, the app should issue `https://` directly. Low priority for v1 but worth tracking.

**TC3 verdict: PARTIAL** — redirect chain is architecturally correct through the verifiable hops. Final hop to FIP requires manual browser confirmation with real DNS resolution.

### TC4 — DB Verification
```sql
SELECT id, name, endpoint_url, is_active FROM mcp_servers;
→ f2144704-3929-49c7-a489-b041a33674cd | forge-kb | https://api.fortressam.ai/mcp | 1
```
Seeder ran successfully. ✅

### TC5 — ECS Service
```json
{
  "running": 1,
  "desired": 1,
  "taskDef": "arn:aws:ecs:us-east-1:742932328420:task-definition/fait-v2:5"
}
```
Target group health: 1 healthy target (172.31.78.70:8080). 2 targets show unhealthy — these are stale entries from previous task runs, not the current live task.

### TC6 — Log Analysis
**Current task logs (632fe54b) — CLEAN:**
```
[15:13:47 INF] Seeded forge-kb mcp_servers entry
[15:13:47 WRN] Overriding HTTP_PORTS '8080' and HTTPS_PORTS ''. Binding to values defined by URLS instead 'http://+:8080'.
```

Prior tasks in the 30-min window showed MySQL `Unable to connect to any of the specified MySQL hosts` errors. These were from crashed tasks that had an incorrect DB connection string pointing to `localhost` instead of Aurora. Those tasks crashed and ECS replaced them. **The current running task does not exhibit this error** — it seeded successfully and is running cleanly.

---

## Issues Found

### ISSUE-1: Stale unhealthy ALB targets [MINOR]
- **What:** Target group `fait-v2-dev-tg` shows 3 registered targets; 2 are unhealthy (IPs from previous task runs)
- **Expected:** Only the current task's IP registered and healthy
- **Impact:** ALB deregisters unhealthy targets automatically; no user impact. ECS manages this.
- **Action:** Auto-resolves. No action needed unless it persists > 30 min.

### ISSUE-2: App generates `http://` redirect URLs behind ALB [MINOR / WARN]
- **What:** `/` redirect returns `Location: http://fait-v2...` instead of `https://`
- **Expected:** With proper `UseForwardedHeaders` config, should be `https://`
- **Impact:** Browser recovers cleanly via ALB HTTP→HTTPS 301. No functional breakage.
- **Action:** Tony should review `ForwardedHeaders` middleware config. Low priority.

### ISSUE-3: DNS not publicly resolvable from test host [TEST CONSTRAINT]
- **What:** `fait-v2.dev.fortressam.ai` doesn't resolve via WSL2 or host browser
- **Impact:** TC1/TC2/TC3 tested via ALB Host-header injection; functionally equivalent but TC3 final hop to FIP unconfirmable
- **Action:** Fred to confirm full redirect chain in a browser session manually.

---

## Post-Auth Sign-Off Required

Per FIP SSO Auth Testing rules, authenticated flow (Path 2) **cannot be tested without Entra MFA credentials**.

**Fred must manually confirm:**
1. Visit `https://fait-v2.dev.fortressam.ai/`
2. Verify redirect lands at FIP login (`fip.dev.fortressam.ai`) — not a loop
3. Complete Entra MFA login
4. Verify you land in FAIT v2 app itself (not a redirect loop, not app selector)
5. Verify a conversation can be started (basic smoke test of the app shell)

**Do not mark ADO#2887 Done until Fred confirms the above.**

---

## Summary

| Category | Result |
|----------|--------|
| Service alive | ✅ Yes — ECS running, ALB healthy, `OK` health response |
| forge-kb seeded | ✅ Yes — DB row confirmed, CloudWatch confirms `[INF] Seeded forge-kb mcp_servers entry` |
| DB connectivity | ✅ Yes — current task connects successfully (prior crashed tasks had bad connection string, resolved in this deploy) |
| Auth flow (unauthenticated) | ✅ Redirect chain correct through verifiable hops |
| Auth flow (post-auth) | ⚠️ REQUIRES FRED MANUAL SIGN-OFF |
| Critical errors | ✅ None in current task |

---

*QA: Natasha Romanoff — Black Widow | Trust nothing. Verify everything.*
