# QA Report — WI#974: Quote Scraper Auth Header Fix

**Analyst:** Black Widow (Natasha Romanoff)  
**Date:** 2026-03-20  
**Environment:** famos-dev (ECS task-definition famos-dev:4)  
**Verdict:** ✅ PARTIAL PASS — T1–T4 all PASS; full E2E scrape requires Fred's manual sign-off

---

## Summary

The auth header fix in `Program.cs` is confirmed deployed and correct. The FortressApi HttpClient now
sends `apiKey` / `apiSecret` (camelCase) instead of `X-Api-Key` / `X-Api-Secret`. All automated checks
pass. Full end-to-end verification (carrier selection → PDF upload → result polling) requires an Entra
MFA login that is outside QA scope.

---

## Test Results

### T1 — Health Check
```
curl -sk -o /dev/null -w "Health: %{http_code}\n" https://famos.dev.fortressam.ai/health
```
**Result:** `Health: 200` ✅

---

### T2 — ECS Running Image
```
Cluster:    fortress-tools-cluster
Service:    famos-dev
Task def:   famos-dev:4
Image:      742932328420.dkr.ecr.us-east-1.amazonaws.com/famos-web:latest
Digest:     sha256:5da22c0710261bd209a2f7d2de20a45031bc29a552bae6fc240559bf7bee027b
ECR tags:   dev-latest, latest
Pushed:     2026-03-20T18:12:33 EDT
Task start: 2026-03-20T18:13:46 EDT
```

Cross-check: `git log --oneline -1` → `b066b80 WI974: fix Quote Scraper auth headers`  
ECR image was pushed at 18:12, task started at 18:13 — same deploy window. Running image matches WI974 commit. ✅

> **Note:** ECR image carries `dev-latest` / `latest` tags (no commit-hash tag). Confirmed via git HEAD
> cross-reference rather than direct tag match.

---

### T3 — Header Names in Source
```
grep -n "apiKey\|apiSecret\|X-Api-Key\|X-Api-Secret\|DefaultRequestHeaders" \
  ~/projects/fip/famos/src/FamOs.Web/Program.cs | head -8
```
**Result:**
```
137:    c.DefaultRequestHeaders.Add("apiKey",
141:    c.DefaultRequestHeaders.Add("apiSecret",
154:        c.DefaultRequestHeaders.Add("Authorization", $"Bearer {hubspotKey}");
```

✅ `apiKey` and `apiSecret` (camelCase) confirmed. No `X-Api-Key` / `X-Api-Secret` present.

---

### T4 — Unauthenticated App Shell
```
curl -sk -o /dev/null -w "App shell: %{http_code}\n" -L -H "User-Agent: Mozilla/5.0" \
  https://famos.dev.fortressam.ai/
```
**Result:** `App shell: 200` ✅  

> Returns 200 (Blazor WASM shell served, Entra redirect handled client-side). No 500 errors.

---

## What Requires Fred's Manual Sign-Off

| Test | Reason blocked |
|------|---------------|
| Select carrier (e.g. Progressive) in Quote Scraper UI | Requires Entra MFA login |
| Upload real carrier PDF | Requires authenticated session |
| Poll for scrape result | Requires authenticated session + valid job ID |

**QA bypass (`/qa/login`) is DISABLED in ECS (famos-dev:4) — returns 401.** Cannot simulate auth path programmatically.

**Action needed:** Fred to log in, navigate to Quote Scraper, upload a real carrier PDF, and confirm a result is returned. The auth header fix is the only code change — if the API call succeeds (no 401/403 from Fortress API), WI974 is done.

---

## Verdict

| Test | Result |
|------|--------|
| T1 Health | ✅ 200 |
| T2 Image tag / commit | ✅ b066b80 deployed (18:12 UTC-4) |
| T3 Header names in source | ✅ apiKey / apiSecret confirmed |
| T4 App shell | ✅ 200 |
| Full E2E scrape | ⏳ Requires Fred manual sign-off |

**PARTIAL PASS** — All automated gates green. Pipeline holds for Fred's E2E confirmation before closing WI974.

---

*"I verified everything I could without an Entra login. The fix is in, the code is right, the service is healthy. The rest is on Fred."*  
*— Natasha*
