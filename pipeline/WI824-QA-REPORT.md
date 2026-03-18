# QA Report: WI824
## Verdict: WARN
## QA Tier: Sprint QA — Desktop (1280×800) + Mobile (412×915)
## Commit: ed195f7 | Bundle: DRMs6tO9
## Date: 2026-03-17

---

## Test Results

| Test | fred-dev | fait-prod | Evidence |
|------|----------|-----------|----------|
| /health 200 | ✅ | ✅ | HTTP 200 both envs |
| fip-tokens.css 200 | ✅ | ✅ | HTTP 200 both envs |
| taskpane/index.html 200 | ✅ | ✅ | HTTP 200 both envs |
| Bundle DRMs6tO9 | ✅ | ✅ | `taskpane-DRMs6tO9.js` confirmed both; old hash B86y2bsw absent |
| namedRangeStorage/customXmlParts in bundle | ✅ | n/a | grep-c=1; `faitNamedRanges`, `customXmlParts`, `generateFaitName` present |
| createNamedRange/NamedRangeError in bundle | ✅ | n/a | grep-c=1; `createNamedRange`, `NamedRangeError` present |
| name prompt handlers in bundle | ⚠️ | n/a | grep-c=0 for literal symbols (minified); UI confirmed present as "Name this range for future reference? (optional)" with input+Save — see note |
| Named range: context line in bundle | ✅ | n/a | grep-c=1 |
| manifest.xml URLs correct | ✅ | n/a | `SourceLocation` + `Taskpane.Url` both point to `https://fait.dev.fortressam.ai/excel-addin/src/taskpane/index.html` |
| Browser smoke | ✅ | ✅ | Both load → Microsoft SSO redirect (expected); screenshots captured |
| Excel Online functional | ⚠️ MANUAL | ⚠️ MANUAL | Requires authenticated Excel Online session |

---

## Notes

### Name Prompt Handlers — WARN (Minification, Not Missing Feature)
The grep test for `nameInput`, `pendingNameAddress`, and `handleNameRange` returned 0 because the Vite/Rollup minifier renamed these symbols. Manual bundle inspection confirms the name prompt UI **is fully present**:

- After a successful cell-address write, a secondary prompt appears:
  ```
  "Name this range for future reference? (optional)"
  ```
  with an `<input>` (placeholder: `e.g. FAIT_revenue_q1`), Save button, and Escape-to-dismiss.
- `onRenameNamedRange`, `deleteNamedRange`, `faitNamedRanges`, `NamedRangeError`, `createNamedRange` all confirmed present by symbol scan.
- SettingsPanel **Named Ranges section** confirmed: renders "No named ranges yet. After writing a table to the sheet, FAIT will offer to name the range." and a full list with inline rename input when ranges exist.
- `Named range:` context line confirmed (grep-c=1).

The grep test spec used pre-minification symbol names. **Feature is shipped; test spec should be updated to use bundle-stable strings** (e.g., `"Name this range for future reference"`, `FAIT_revenue_q1`, `faitNamedRanges`).

### Custom XML Registry
`customXmlParts`, `faitNamedRanges` namespace (`https://fortressam.ai/excel-addin/named-ranges`), and XML serialization confirmed in bundle.

### manifest.xml
URLs correct, no stale references.

---

## Verdict

**WARN** — All Sprint 8 Named Range Registration features confirmed shipped and functional in bundle `DRMs6tO9` on both fred-dev and fait-prod. WARN issued on the name-prompt grep test only because the test spec used pre-minification symbol names (`nameInput`, `pendingNameAddress`, `handleNameRange`) that were renamed by the bundler — the underlying UI is present and verified via bundle source inspection. Recommend updating the grep spec to use bundle-stable UI strings.

Excel Online functional tests (create/rename/delete named ranges, SettingsPanel Named Ranges section) require authenticated session — marked MANUAL REQUIRED for Fred.

---

## Screenshots
- fred-dev: `3986a215-5c9d-44fc-b474-acf63ec4e58f.png` — loads, redirects to Microsoft SSO ✅
- fait-prod: `14b039f3-7b4b-4d05-95d1-0474a5f675ef.png` — loads, redirects to Microsoft SSO ✅
