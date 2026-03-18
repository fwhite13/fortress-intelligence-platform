# QA Report: WI825
## Verdict: PASS
## QA Tier: Sprint QA
## QA Agent: Black Widow (Natasha Romanoff)
## Date: 2026-03-17

---

## What Was Deployed

**WI825** — Reactive Workbook Watching: `onChanged` subscription, `isFaitWriting` loop-prevention singleton, watch config UI in ChatPanel (👁 toggle), watch status bar, `enableEvents` guard in write functions, `setFaitWriting` wrapping in WriteSuggestionsDialog. ExcelApi bumped to 1.13.

**Commit:** `588fa6c` | **Bundle:** `EkUBIBFc`
**fred-dev:** fred-dev:118 | **fait-prod:** fait-prod:27

---

## Test Results

| Test | fred-dev | fait-prod | Evidence |
|------|----------|-----------|----------|
| /health 200 | ✅ | ✅ | HTTP 200 both envs |
| fip-tokens.css 200 | ✅ | ✅ | HTTP 200 both envs |
| taskpane/index.html 200 | ✅ | ✅ | HTTP 200 both envs |
| Bundle EkUBIBFc | ✅ | ✅ | `taskpane-EkUBIBFc.js` confirmed both; old `DRMs6tO9` absent |
| isFaitWriting/setFaitWriting in bundle | ✅ | n/a | Minified to `de()` — `if(de())return` guard confirmed (count: 1), `!an&&!de()` write guard confirmed (count: 1) |
| Watch Mode UI strings in bundle | ✅ | n/a | `watchPulse` ×3, `👁 Watch Mode` ×1, `Watch mode` ×1 — total string matches: 2 via grep-c |
| enableEvents in bundle | ✅ | n/a | count: 2 |
| ExcelApi 1.13 in live manifest | ✅ | n/a | `<Set Name="ExcelApi" MinVersion="1.13"/>` |
| manifest URLs correct | ✅ | n/a | SourceLocation and Taskpane.Url both point to `https://fait.dev.fortressam.ai/excel-addin/src/taskpane/index.html` |
| Browser smoke | ✅ | ✅ | Microsoft Entra sign-in page (expected — auth-gated). Screenshots captured. |
| Watch mode functional | ⚠️ MANUAL | ⚠️ MANUAL | Requires Excel Online with active workbook. onChanged subscription, loop-prevention, triggerWatchAnalysis require manual Excel session. |

---

## Findings & Notes

### isFaitWriting / setFaitWriting — Minification Note
The bundle grep for literal `isFaitWriting` returns 0 because the Vite/Rollup minifier mangled the symbol names. However, the loop-prevention logic is confirmed present:
- `if(de())return` — the watch handler early-exits when FAIT is writing (minified `isFaitWriting` check)
- `!an&&!de()` — the `triggerWatchAnalysis` function guards with both `enableEvents` and the writing singleton
- `console.warn(\`FAIT watch: failed to register handler:\`)` and `console.warn(\`FAIT watch: trigger analysis failed:\`)` — watch infrastructure debug strings intact

This is expected behaviour for a production-minified bundle. The source-level symbol names do not survive minification; the runtime logic does.

### Watch Mode UI — Fully Confirmed
- `👁 Watch Mode` config panel heading present
- `Start Watching` / `Stop watching` / `Use selection` controls present
- `watchPulse` CSS animation present (×3 — button indicator + status bar indicator)
- Watch status bar: `Watching: <range>` + trigger count + last trigger timestamp
- Watch mode ON tooltip: `Watch mode ON — N trigger(s)`
- Watch mode OFF tooltip: `Enable watch mode — FAIT reacts to cell changes`

### enableEvents Guard
`enableEvents` appears ×2 — consistent with guarding both the write functions and the `WriteSuggestionsDialog`.

### ExcelApi 1.13
`<Set Name="ExcelApi" MinVersion="1.13"/>` confirmed in live manifest.

### Manifest URLs
Both `SourceLocation` and `bt:Url id="Taskpane.Url"` correctly reference fred-dev origin. No stale localhost or wrong-env URLs.

### Browser Smoke
Both `https://fait.dev.fortressam.ai` and `https://fait.fortressam.ai` load the Microsoft Entra sign-in page. This is expected behaviour — the app is auth-gated. Both return 200 on health/static assets.

---

## Manual Testing Required

| Item | Why Manual |
|------|------------|
| `onChanged` event subscription fires | Requires Excel Online with active workbook |
| Loop-prevention: write op does not re-trigger watch | Requires live Excel session + active watch |
| `triggerWatchAnalysis` invokes FAIT chat | Requires live Excel session |
| Watch status bar updates on trigger | Requires live Excel session |
| `setFaitWriting` wrapping in WriteSuggestionsDialog | Requires write operation in Excel |

These items cannot be verified via bundle inspection or browser automation without an active Excel Online session. Mark for Fred's manual sign-off.

---

## Verdict

**PASS** — All automated Sprint QA checks pass. Bundle `EkUBIBFc` deployed to both environments. Watch Mode UI strings, `enableEvents` guard, and loop-prevention logic confirmed in minified bundle. ExcelApi 1.13 in manifest. Health baseline clean. Browser smoke clean. Watch mode functional testing marked MANUAL REQUIRED (Excel Online session needed).
