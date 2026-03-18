# QA Report: WI813
## Verdict: WARN
## QA Tier: Sprint QA
## Date: 2026-03-16
## Tester: Black Widow (Natasha Romanoff) — qa-analyst

---

## Test Results

| Test | Result | Evidence |
|------|--------|----------|
| index.html valid HTML (Office.js + module script) | ✅ | Office.js CDN script present; `<script type="module" src="/excel-addin/assets/taskpane-t0ZrHc1u.js">` confirmed; no plain `taskpane.js` reference |
| Health endpoint 200 | ✅ | `{"status":"healthy","timestamp":"2026-03-16T16:34:52.0682866Z"}` |
| fip-tokens.css 200 | ✅ | HTTP 200 |
| Hashed JS asset served 200 | ✅ | `https://fait.dev.fortressam.ai/excel-addin/assets/taskpane-t0ZrHc1u.js` → HTTP 200 |
| manifest.xml URLs updated (no bare directory) | ⚠️ WARN | `SourceLocation` = `https://fait.dev.fortressam.ai/excel-addin/` (bare dir); `Taskpane.Url` = `https://fait.dev.fortressam.ai/excel-addin/` (bare dir). Neither updated to `/src/taskpane/index.html` |
| FAIT UI loads in browser | ✅ | Redirects to Microsoft Entra SSO login — app running, auth wired correctly. Screenshot captured. |
| Excel Online smoke test | ⚠️ MANUAL REQUIRED | Requires sideloading manifest into Excel Online with authenticated Microsoft account. Taskpane HTML endpoint returning 200 with valid content (Test #1) is the automated proxy for this test. |

---

## Evidence Details

### Test 1 — index.html Content (first 50 lines)
```html
<!DOCTYPE html>
<html lang="en">
  <head>
    <meta charset="UTF-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>FAIT for Excel</title>
    <link rel="preconnect" href="https://fonts.googleapis.com" />
    <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin />
    <link href="https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700&display=swap" rel="stylesheet" />
    <!-- Office JS — required for all Office Add-ins -->
    <script src="https://appsforoffice.microsoft.com/lib/1/hosted/office.js" type="text/javascript"></script>
    <script type="module" crossorigin src="/excel-addin/assets/taskpane-t0ZrHc1u.js"></script>
    <link rel="stylesheet" crossorigin href="/excel-addin/assets/taskpane-DarIh3SN.css">
  </head>
  <body>
    <div id="root"></div>
  </body>
</html>
```
✅ Office.js CDN present. ✅ Hashed module script. ✅ No IIFE / plain `taskpane.js`.

### Test 5 — manifest.xml URL Values
```
<SourceLocation DefaultValue="https://fait.dev.fortressam.ai/excel-addin/"/>
<SourceLocation resid="Taskpane.Url"/>
<bt:Url id="Taskpane.Url" DefaultValue="https://fait.dev.fortressam.ai/excel-addin/"/>
```
⚠️ Both still point to the bare directory URL. Neither has been updated to `.../src/taskpane/index.html`.

**Note:** `manifest.local.xml` (the local dev sideloading file specified in the WI as a deliverable) returns 404 at `https://fait.dev.fortressam.ai/excel-addin/manifest.local.xml`. This is expected — local-only files may not be served — but is noted for completeness.

### Test 6 — Browser Smoke Test
FAIT app at `https://fait.dev.fortressam.ai` redirects to Microsoft Entra SSO login page, confirming:
- App is running and responding
- Auth middleware is active
- No 500/crash on root request

Screenshot: `~/.openclaw/media/browser/a594de64-c77e-40f3-b611-d1587377616f.png`

---

## Issues Found

### ⚠️ WARN — manifest.xml URLs not updated to index.html path (Non-blocking)

**Severity:** WARN (non-blocking for Vite build foundation; blocking for Excel sideloading in production)

**Finding:** Both `SourceLocation` and `Taskpane.Url` in `manifest.xml` still reference the bare directory URL (`/excel-addin/`), not the new explicit path (`/excel-addin/src/taskpane/index.html`).

**Impact:**
- The Vite build itself is working correctly — `index.html` serves valid content with hashed assets
- However, any user sideloading `manifest.xml` into Excel will get the bare directory URL. Whether Excel resolves this to `index.html` depends on server config (nginx may serve it correctly as a directory index, but this is not the intended production behavior per WI813 spec)
- `manifest.local.xml` is not accessible at the served URL (expected for local-only file, but should be confirmed as intentional)

**Recommendation:** Update `manifest.xml` `SourceLocation` and `Taskpane.Url` to explicitly point to `.../src/taskpane/index.html`. This should be a follow-up WI or addressed before production sideloading.

---

## Verdict

**WARN** — The core Vite build foundation is solid. The primary acceptance criteria pass:
- ✅ `index.html` serves valid HTML with Office.js CDN + hashed module script bundle
- ✅ App is healthy
- ✅ FipShared RCL is present
- ✅ Hashed JS assets are served (200)
- ✅ FAIT base app responds correctly in browser

The only gap is `manifest.xml` not updated to reference the explicit `index.html` path, which is the final step to fully realize the WI813 intent. This is non-blocking for the Vite build foundation itself but should be resolved before Excel Online sideloading is validated by Fred.

**Not recommending rollback.** The build foundation changes (Vite serving, hashed assets, OfficeRuntime fallback fix) are working correctly. The manifest URL update is a targeted follow-up fix.

## Cycle 2 Re-verification

| Test | Result | Evidence |
|------|--------|----------|
| manifest.xml SourceLocation URL | ✅ | `https://fait.dev.fortressam.ai/excel-addin/src/taskpane/index.html` |
| manifest.xml Taskpane.Url | ✅ | `https://fait.dev.fortressam.ai/excel-addin/src/taskpane/index.html` |
| Health endpoint | ✅ | `{"status":"healthy","timestamp":"2026-03-16T16:45:18.1011352Z"}` |
| fip-tokens.css | ✅ | HTTP 200 |
| taskpane index.html | ✅ | HTTP 200 |

## Final Verdict: PASS
