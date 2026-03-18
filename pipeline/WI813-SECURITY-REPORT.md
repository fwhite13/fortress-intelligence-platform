# Security Report: WI813
## Verdict: PASS
## Scan Scope: Changed files (medium risk)
## Files Scanned: package.json, vite.config.ts, settings.ts, storage.ts, manifest.xml, manifest.local.xml

---

## Stage 1 — Discovery

**External dependencies added:**
- `vite-plugin-mkcert` ^1.17.6 (devDependency) — generates locally-trusted HTTPS cert for dev server

**External dependencies removed:**
- `@microsoft/office-js` — unsupported npm mirror package removed; CDN loading retained in index.html

**URL patterns:** All manifest URLs point to `fait.dev.fortressam.ai` (production) or `localhost:3000` (local dev only). No unexpected domains.

**Storage patterns:** `settings.ts` and `storage.ts` — API key stored in `OfficeRuntime.storage` (in Excel) or `localStorage` (browser fallback). No network transmission of stored values in changed code.

**Script execution:** `build:copy` script — local development convenience script. Copies `dist/*` to adjacent path in monorepo (`../fip/fait/src/FortressAI.Web/wwwroot/excel-addin/`). No network access.

---

## Stage 2 — Analysis

### package.json
- `@microsoft/office-js` removed ✅ — was an unsupported stale npm mirror; correct to remove
- `vite-plugin-mkcert` added as devDependency — dev-only, does not ship in build output
- `build:copy` path: `../fip/fait/src/FortressAI.Web/wwwroot/excel-addin/` — relative path to known sibling directory in monorepo. No path traversal risk. Dev convenience script only.
- Runtime dependencies reduced to `react` + `react-dom` only ✅

### vite.config.ts
- `base: '/excel-addin/'` — correct, matches deployment path
- `mkcert()` — devDependency, runs only during `npm run dev`. Generates a local CA cert stored in user's trust store. Standard practice for Office Add-in dev. Not included in build output.
- No eval, no arbitrary code execution, no unexpected network calls in config

### settings.ts / storage.ts
- localStorage shim is semantically appropriate: in Excel Online, `OfficeRuntime.storage` is already backed by localStorage per Microsoft docs. The shim is equivalent for the web scenario.
- `fait_api_key` stored in localStorage when running outside Excel — acceptable. This is a developer/user-provided key, not a service credential.
- No hardcoded secrets, tokens, or passwords
- No eval or dangerous DOM patterns

### manifest.xml
- All URLs: `fait.dev.fortressam.ai` only — expected production domain ✅
- `AppDomains`: single entry (`fait.dev.fortressam.ai`) ✅

### manifest.local.xml
- URLs: `fait.dev.fortressam.ai` (icons, support) + `localhost:3000` (taskpane, commands) ✅
- `AppDomains`: `fait.dev.fortressam.ai` + `localhost:3000` — localhost trust is appropriate for a local dev manifest; this file is not for production use ✅
- Display name includes "(Local Dev)" to reduce sideload confusion ✅

---

## Stage 3 — Verification

**Secrets/tokens grep:** `src/taskpane/services/storage.ts` line 16 — `const KEY = 'fait_api_key'` — this is a localStorage key name, not a credential. CLEAN.

**eval/dangerous patterns:** CLEAN — no `eval`, `innerHTML`, `dangerouslySetInnerHTML`, or `document.write` in changed files.

**vite-plugin-mkcert:** `v1.17.10` available (spec pins `^1.17.6`). Package is the standard community plugin for Vite HTTPS dev; 1.2M+ weekly downloads; well-maintained GitHub repo. No known CVEs.

**Manifest URLs verified:** All URLs are either `fait.dev.fortressam.ai` (controlled domain) or `localhost:3000` (local dev manifest only). No unexpected external domains.

---

## Stage 4 — Findings

### Critical
None.

### High
None.

### Medium (WARN)
None.

### Low / Info
- **INFO:** `manifest.local.xml` includes `fait.dev.fortressam.ai` in `AppDomains`. This allows the local dev taskpane (at localhost:3000) to interact with the production API domain. Expected and intentional — dev environment calls the dev API endpoint.

---

## Verdict: PASS

No blocking findings. The changes are a build configuration refactor with no new attack surface introduced. The `localStorage` shim is semantically correct for the use case. `vite-plugin-mkcert` is dev-only and does not affect production builds. All manifest URLs are controlled domains. API key storage in localStorage is pre-existing behavior made more resilient, not a regression.

**Pipeline may advance to APPROVE.**
