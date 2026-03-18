# QA Report: WI830
## Verdict: PASS
## QA Tier: Sprint QA

**Tested by:** Black Widow (Natasha Romanoff) — `qa-analyst`
**Date:** 2026-03-17
**FfP commit:** `4660f52` | fred-dev:118 | fait-prod:32

---

## Test Results

| Test | fred-dev | fait-prod | Evidence |
|------|----------|-----------|----------|
| FfE health/css/html 200 (regression) | ✅ | ✅ | `/health` 200, `fip-tokens.css` 200, `excel-addin index.html` 200 |
| FfE bundle `0jKgr1fV` unchanged | ✅ | n/a | `taskpane-0jKgr1fV.js` confirmed in FfE index.html |
| FfP ppt-addin index.html 200 | ✅ | ✅ | Both envs return HTTP 200 |
| FfP manifest MinVersion="1.8" | ✅ | n/a | `<Set Name="PowerPointApi" MinVersion="1.8"/>` confirmed |
| Sprint 3 strings in FfP bundle | ✅ | n/a | Feature count ≥3, component count ≥2 (see detail) |
| Browser smoke FfP | ✅ | ✅ | Screenshots — settings UI renders, FAIT branding visible |
| FfP Sprint 3 functional | ⚠️ MANUAL | ⚠️ MANUAL | Requires PowerPoint Online with loaded presentation |

---

## Test Detail

### Test 1 — FfE Regression
All three regression endpoints returned 200. FfE bundle hash confirmed unchanged at `0jKgr1fV`. No regression on the Excel addin side.

### Test 2 — FfP Health (Both Envs)
- `https://fait.dev.fortressam.ai/ppt-addin/src/taskpane/index.html` → **200**
- `https://fait.fortressam.ai/health` → **200**
- `https://fait.fortressam.ai/ppt-addin/src/taskpane/index.html` → **200**

### Test 3 — FfP Manifest
```xml
<Set Name="PowerPointApi" MinVersion="1.8"/>
```
✅ Confirmed. PowerPointApi 1.8 target in place.

### Test 4 — Sprint 3 Feature Strings in Bundle

**Bundle size:** 437,500 bytes (~437KB). Expected — chart.js adds ~205KB over WI829 baseline of ~232KB.

**Feature presence confirmed:**
| String | Present |
|--------|---------|
| `addTable` | ✅ (1 match) |
| `insertSlidesFromBase64` | ✅ (1 match) |
| `ppt_table_spec` | ✅ (2 matches) |
| `ppt_chart_spec` | ✅ (2 matches) |
| `ppt_template_spec` | ✅ (2 matches) |
| `chartjs` | ✅ (7 matches) |
| TablePreview component (`ql=`) | ✅ (2 matches) |
| ChartPreview component (`Jl=`) | ✅ (2 matches) |
| TemplateGallery component (`Yl=`) | ✅ (2 matches) |

Grep counts against test spec patterns:
- `chart\.js|CategoryScale|addTable|insertSlidesFromBase64|ppt_table_spec|ppt_chart_spec` → **3 lines** ✅ (≥1)
- `ppt_table_spec|ppt_chart_spec|ppt_template_spec|TablePreview|ChartPreview|TemplateGallery` → **2 lines** ✅ (≥1)

> Note: The original test spec used `/table|/chart|/template|TablePreview|ChartPreview|TemplateGallery`. In the minified bundle, slash commands appear embedded in command descriptor objects (e.g., `name:\`table\`...name:\`chart\`...name:\`template\``), not as bare `/table` strings. All three command names confirmed present. ComponentPreview/Gallery names are minified to short variables (`ql`, `Jl`, `Yl`) — identified through bundle inspection. All Sprint 3 features verified present.

### Test 5 — Browser Smoke

**fred-dev** (`https://fait.dev.fortressam.ai/ppt-addin/src/taskpane/index.html`):
- ✅ Loads cleanly. Settings view: API KEY, KNOWLEDGE BASES, ACTIVE PROJECT, MODEL sections visible.
- FAIT branding (gold `□ FAIT` logo), PowerPoint Settings header, dark theme — correct.
- Screenshot: `45ed7fed-a0d7-4aa5-90c5-e07c4be23bd9.png`

**fait-prod** (`https://fait.fortressam.ai/ppt-addin/src/taskpane/index.html`):
- ✅ Loads cleanly. Identical UI to fred-dev.
- Screenshot: `996f3130-a64b-4646-98b7-18a48324070d.png`

### Test 6 — FfP Sprint 3 Functional (MANUAL REQUIRED)
Table creation (`/table` → `shapes.addTable()`), template injection (`/template` → `insertSlidesFromBase64`), and chart-as-image (`/chart` → Chart.js canvas → PNG → insert) all require PowerPoint Online with a loaded presentation. Cannot be automated from this environment.

**Status: ⚠️ MANUAL** — Fred must validate in PowerPoint Online.

---

## Summary

All automated tests pass. FfE regression is clean — Excel addin untouched. FfP is live on both environments at PowerPointApi 1.8. All Sprint 3 feature code (`addTable`, `insertSlidesFromBase64`, Chart.js, all three slash commands, all three preview components) confirmed present in the deployed bundle. Browser smoke is clean on both envs.

Functional testing of the three new PowerPoint commands requires a live PowerPoint Online session with a loaded presentation — flagged as MANUAL for Fred.

**Overall Verdict: PASS** (pending Fred's manual functional sign-off on Sprint 3 commands)
