# QA Report: WI827
## Verdict: PASS
## QA Tier: Sprint QA
**QA Agent:** Black Widow (Natasha Romanoff)
**Date:** 2026-03-17
**HEAD Commit:** `0671ddc` | **Bundle:** `0jKgr1fV`
**Environments:** fred-dev (fred-dev:118) | fait-prod (fait-prod:29)

---

## Test Results

| Test | fred-dev | fait-prod | Evidence |
|------|----------|-----------|----------|
| /health 200 | ✅ | ✅ | HTTP 200 both envs |
| fip-tokens.css 200 | ✅ | ✅ | HTTP 200 both envs |
| taskpane/index.html 200 | ✅ | ✅ | HTTP 200 both envs |
| Bundle 0jKgr1fV | ✅ | ✅ | `taskpane-0jKgr1fV.js` confirmed both; old `Bu81Do3I` absent |
| /formula + formula_spec strings in bundle | ✅ | n/a | Count: 6 (≥1 required) |
| Formula preview UI strings in bundle | ✅ | n/a | Count: 1 (≥1 required) |
| ExcelApi 1.13 in manifest | ✅ | n/a | `<Set Name="ExcelApi" MinVersion="1.13"/>` |
| Browser smoke | ✅ | ✅ | Both envs load → MS auth redirect (expected, auth-gated) |
| /formula functional | ⚠️ MANUAL | ⚠️ MANUAL | Requires Excel Online + active workbook session |

---

## Test Detail

### Test 1 — Health Baseline
All 6 endpoints returned HTTP 200 across both environments. No degradation detected.

### Test 2 — Bundle Hash
- fred-dev: `taskpane-0jKgr1fV.js` ✅
- fait-prod: `taskpane-0jKgr1fV.js` ✅
- Old bundle `Bu81Do3I` not present in either env.

### Test 3 — Sprint 11 Feature Strings in Bundle (fred-dev)
- **`/formula` command + `formula_spec` + `FAIT_SCRATCH` strings:** Count = **6** (threshold ≥ 1) ✅
  - Strings matched: `/formula`, `formula_spec`, `FAIT_SCRATCH` confirmed present in minified bundle
- **Formula preview flow strings (`Write Formula`, `Preview`, `scratch`):** Count = **1** (threshold ≥ 1) ✅

### Test 4 — ExcelApi 1.13 Manifest
```xml
<Set Name="ExcelApi" MinVersion="1.13"/>
```
ExcelApi 1.13 requirement intact. ✅

### Test 5 — Browser Smoke
- **fred-dev (`https://fait.dev.fortressam.ai`):** Loads → redirects to Microsoft Entra sign-in. App is live and auth-gating correctly. ✅
- **fait-prod (`https://fait.fortressam.ai`):** Loads → redirects to Microsoft Entra sign-in. App is live and auth-gating correctly. ✅
- Screenshots captured: `2662b0d0-27c9-40e0-8368-bb34c656f3a3.png` (fred-dev), `716b87f1-e881-4d85-aba9-91d5643c275c.png` (fait-prod)

---

## Manual Required

| Item | Status | Notes |
|------|--------|-------|
| `/formula` slash command trigger | ⚠️ MANUAL | Requires Excel Online + active workbook session |
| `formula_spec` parser output | ⚠️ MANUAL | Requires live AI conversation context |
| `__FAIT_SCRATCH__` veryHidden sheet preview | ⚠️ MANUAL | Requires Excel context + formula suggestion flow |
| `writeFormula()` cell write + `setFaitWriting` guard | ⚠️ MANUAL | Requires Excel context |
| `comments.add()` split sync | ⚠️ MANUAL | Requires Excel context |

---

## Verdict

**PASS** — All automated checks pass. Bundle `0jKgr1fV` confirmed on both environments. Sprint 11 feature strings (`/formula`, `formula_spec`, `FAIT_SCRATCH`, preview flow) verified present in minified bundle. ExcelApi 1.13 manifest requirement intact. Both environments healthy and serving correctly. Functional `/formula` flow requires Excel Online session — marked MANUAL REQUIRED per established Sprint QA policy.
