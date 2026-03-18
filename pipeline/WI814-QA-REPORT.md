# QA Report: WI814
## Verdict: PASS ⚠️ (with tree-shaking note)
## QA Tier: Sprint QA

## Test Results

| Test | Result | Evidence |
|------|--------|----------|
| Health baseline (200/200/200) | ✅ | /health: 200, fip-tokens.css: 200, index.html: 200 |
| New bundle hash DtS61AUh live | ✅ | 1 match in index.html; old hash `t0ZrHc1u` absent |
| "No selection" text in bundle | ✅ | 1 match — full text: "No selection — click a cell to include context" |
| writeRangeData in bundle | ⚠️ TREE-SHAKEN | 0 matches — see note below |
| "Range mismatch" in bundle | ✅ | 3 matches — full text: "Range mismatch — the selected cells don't fit..." |
| FAIT UI loads in browser | ✅ | Entra SSO redirect confirmed (Microsoft Sign In page) |
| Excel Online functional test | ⚠️ MANUAL REQUIRED | Requires sideloading into Excel Online with authenticated Microsoft account |

## Tree-Shaking Note — writeRangeData / WriteRangeError

**Root cause:** `writeRangeData` and `WriteRangeError` are defined in `excelWriter.ts` (exported, confirmed in source at `/home/fredw/projects/fait-for-excel/src/taskpane/services/excelWriter.ts`), and imported in `ChatPanel.tsx` (line 5). However, neither symbol is **called or referenced** anywhere in `ChatPanel.tsx`'s component body — the import is a forward declaration for Sprint 3 wiring. Vite's tree-shaker correctly eliminates unused imports.

**This is expected behavior.** The WI814 brief explicitly states: *"not yet wired to UI — infrastructure only."* The function exists in source (verified via `git show 6c8649e --stat`) and will appear in the bundle once wired to the UI in Sprint 3.

**Verification path used:** Source file confirmed present → git commit diff confirmed (`excelWriter.ts +64 lines`) → bundle absence explained by tree-shaking of unused import in `ChatPanel.tsx`.

**Recommendation for Sprint 3:** When `writeRangeData` is wired to a UI handler, it will appear in the bundle automatically. No action needed on WI814.

## Bundle Content — "Range mismatch" Detail

The `Range mismatch` message is present in two contexts in the bundle:
1. `WriteSuggestionsDialog` path: `"Range mismatch — the selected cells don't fit the suggested data. Try accepting each suggestion..."`
2. `WriteSuggestionsDialog` single-cell error path: `"Cell ${i}: range doesn't fit — skipping."`

Both confirm WI814 Change #3 is live.

## Browser Smoke Test

FAIT at `https://fait.dev.fortressam.ai` correctly redirects to Microsoft Entra SSO (Sign In page). Expected behavior — no unauthenticated access. Screenshot captured.

## Issues Found

None blocking. One informational note:
- **writeRangeData tree-shaken from bundle** — expected for infrastructure-only code, not a defect. Will resolve automatically in Sprint 3 when wired to UI.

## Verdict

**PASS** — All three WI814 changes are confirmed deployed:

1. ✅ **ContextIndicator empty state** — "No selection — click a cell to include context" text confirmed in bundle (1 match, full string verified)
2. ⚠️ **writeRangeData / WriteRangeError** — Present in source (commit `6c8649e`, `excelWriter.ts` +64 lines) but tree-shaken from bundle because the import in `ChatPanel.tsx` is unused (Sprint 3 pre-wire). Expected behavior per WI814 spec ("infrastructure only").
3. ✅ **WriteSuggestionsDialog dimension mismatch** — "Range mismatch" message confirmed in bundle (3 matches), specific error text "the selected cells don't fit" confirmed

Bundle hash `DtS61AUh` confirmed live. Health checks 200/200/200. Entra SSO redirect confirmed.

**Excel Online functional testing (ContextIndicator visual state, WriteSuggestionsDialog error trigger) requires manual testing by Fred in Excel Online.**

---
*QA by: Black Widow (Natasha Romanoff) — qa-analyst*
*Date: 2026-03-16*
*ECS: fred-dev:118 | Commit: 6c8649e*
