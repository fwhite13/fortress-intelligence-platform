# Review Report: WI813
## Cycle: 1 of 2
## Verdict: NEEDS-CHANGES

## CC Invocation
```bash
cd /home/fredw/projects/fait-for-excel
cat review-brief-wi813.md | claude --model sonnet -p
```

---

## Mandatory Checklist Results

### Path Deviation (dist/src/taskpane/index.html)
**ACCEPTED**

Reed's spec comment was incorrect — Vite v8 (rolldown) preserves the full relative input path, not strips it. Input `src/taskpane/index.html` → output `dist/src/taskpane/index.html`. Tony's attempt at `root: 'src'` correctly identified the right instinct, but rolldown rejects `../public/commands.html` as an entry point, making the flattened path unachievable without source tree restructuring. The longer path is coherent, the manifest URLs are consistent, and deployment integrity is maintained.

- **dist/src/taskpane/index.html exists:** YES
- **manifest.xml URLs match:** YES — both `<SourceLocation>` and `<bt:Url id="Taskpane.Url">` point to `https://fait.dev.fortressam.ai/excel-addin/src/taskpane/index.html`
- **Deployment path integrity:** PASS — `cp -r dist/* wwwroot/excel-addin/` → file served at `/excel-addin/src/taskpane/index.html` ✓

---

### manifest.xml Critical URLs
- **SourceLocation:** `https://fait.dev.fortressam.ai/excel-addin/src/taskpane/index.html` (line 28)
- **Taskpane.Url:** `https://fait.dev.fortressam.ai/excel-addin/src/taskpane/index.html` (line 80)
- **Verdict: PASS**

---

### @microsoft/office-js Removal
- **package.json:** No match — `dependencies` contains only `react` and `react-dom`; `devDependencies` contains only build tools + `@types/office-js` (TypeScript type definitions, correct — NOT the runtime package)
- **source files:** `grep -r "@microsoft/office-js" src/` → no matches
- **Verdict: PASS**

---

### vite.config.ts format override check
- **grep result:** `grep -E "format\s*:" vite.config.ts` → no output
- **rollupOptions block verified:**
  ```ts
  rollupOptions: {
    input: {
      taskpane: 'src/taskpane/index.html',
      commands: 'public/commands.html',
    },
    // No output.format override — defaults to ES modules, which is correct
  },
  ```
- **Verdict: PASS**

---

### storage.ts fix
- **grep result:** `grep -n "OfficeRuntime" src/taskpane/services/storage.ts`
  - Line 1: `// localStorage shim — used when OfficeRuntime is not available (plain browser / dev)`
  - Line 13: `return (window as any).OfficeRuntime?.storage ?? localStorageShim;`
- `localStorageShim` + `getStorage()` pattern matches `settings.ts` exactly. All three exported functions (`getApiKey`, `setApiKey`, `clearApiKey`) use `getStorage()`. No bare `OfficeRuntime.storage.*` calls.
- **Verdict: PASS**

---

### manifest.local.xml AppDomains
- **grep result:** `grep -A2 "AppDomains" manifest.local.xml`
  ```xml
  <AppDomains>
      <AppDomain>https://fait.dev.fortressam.ai</AppDomain>
      <AppDomain>https://localhost:3000</AppDomain>
  </AppDomains>
  ```
- `https://localhost:3000` present ✓
- **Verdict: FAIL** — see Issues Found (Important bug on Commands.Url)

---

### dist/src/taskpane/index.html contents
- **Office.js CDN script:** YES — `<script src="https://appsforoffice.microsoft.com/lib/1/hosted/office.js" type="text/javascript"></script>` in `<head>` ✓
- **script type="module":** YES — `<script type="module" crossorigin src="/excel-addin/assets/taskpane-t0ZrHc1u.js"></script>` with correct `/excel-addin/` base path ✓
- **Verdict: PASS**

---

## Issues Found

### Important (must fix before merge)

**`manifest.local.xml` — Commands.Url uses wrong path prefix**
- **File:** `manifest.local.xml`
- **Line:** 80 (approximately — `<bt:Url id="Commands.Url">`)
- **Issue:** Value is `https://localhost:3000/public/commands.html`. Vite's `publicDir` (default: `public/`) serves files in `public/` at the HTTP **root** path — the `/public/` directory segment does not exist at the HTTP level. `public/commands.html` is served at `https://localhost:3000/commands.html`.
- **Impact:** When a developer sideloads `manifest.local.xml` into Excel Online and opens the add-in, Office attempts to load the ribbon function file from `/public/commands.html` and receives a **404**. Ribbon command buttons (if any) will be broken in local dev.
- **Evidence:** Production `manifest.xml` line 79 correctly uses `https://fait.dev.fortressam.ai/excel-addin/commands.html` (no `/public/` prefix), confirming the local manifest is inconsistent.
- **Required fix:** Change `Commands.Url` in `manifest.local.xml` to `https://localhost:3000/commands.html`

### Critical
None.

### Nitpick
None.

---

## Overall Verdict: NEEDS-CHANGES

**One Important bug — one-line fix.** All core deliverables are correct: HTML entry points work, path deviation is acceptable, manifest.xml production URLs are accurate, `@microsoft/office-js` is gone, `vite.config.ts` has no format override, both `settings.ts` and `storage.ts` use the correct localStorage shim, and the built HTML is valid.

The single issue is `manifest.local.xml` `Commands.Url` having `/public/` in the path, which causes a 404 for the ribbon commands page during local dev sideloading. Fix is surgical: one string change in `manifest.local.xml` line ~80.

**Return to Tony with this single fix. No re-review of unchanged files needed in Cycle 2 — Clint will spot-check only the Commands.Url line.**

---

## Cycle 2 Spot-Check
**Verdict: PASS**

- Commands.Url grep: `<bt:Url id="Commands.Url" DefaultValue="https://localhost:3000/commands.html"/>` — no `/public/` prefix ✓
- Commit scope (git show --stat 024e51e): Only `manifest.local.xml` changed (1 file, 1 insertion, 1 deletion) ✓
- Conclusion: Fix is correct, surgical, and scoped exactly as required.

## Final Verdict: PASS
