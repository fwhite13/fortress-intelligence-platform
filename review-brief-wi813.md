# Review Brief: WI813 — FfE Fix Vite Build Foundation
## Reviewer: Hawkeye (Clint Barton)
## Cycle: 1 of 2

You are Claude Code acting as a senior code reviewer. Read the actual files and perform a thorough review.

## Your Task

Review the WI813 changes in `/home/fredw/projects/fait-for-excel/`. All mandatory checks below must be verified by reading the actual files. Do NOT accept the build report's claims at face value — verify each one.

---

## Files to Read

Read these files now:

1. `vite.config.ts`
2. `package.json`
3. `src/taskpane/services/settings.ts`
4. `src/taskpane/services/storage.ts`
5. `manifest.xml`
6. `manifest.local.xml`
7. `src/taskpane/index.html`
8. `dist/src/taskpane/index.html` (the built output)

---

## Context: What Tony Was Supposed to Do

WI813 spec (Reed Richards) required:
1. Fix Vite build config: switch from `format: 'iife'` + bare `.tsx` entry → HTML entry points
2. Remove `@microsoft/office-js` from npm (CDN only per Microsoft)
3. Fix `OfficeRuntime` fallback `ReferenceError` in `settings.ts` (and `storage.ts`)
4. Add `vite-plugin-mkcert` for local dev HTTPS
5. Create `manifest.local.xml` for dev sideloading
6. Update `manifest.xml` URLs to reflect actual build output path

**Path deviation from spec:** Reed's spec expected `dist/taskpane/index.html`. Tony reports Vite v8 (rolldown) actually produces `dist/src/taskpane/index.html` (preserves full input path). Tony accepted this and updated manifest URLs accordingly.

---

## Mandatory Checks (Verify ALL)

### CHECK 1 — Path Deviation (HIGH)

Tony claims: `dist/src/taskpane/index.html` exists, manifest URLs updated to `/excel-addin/src/taskpane/index.html`.

**Verify:**
- Does `dist/src/taskpane/index.html` exist? (ls output confirms YES)
- Do both manifest.xml URLs (`<SourceLocation>` and `<bt:Url id="Taskpane.Url">`) point to `https://fait.dev.fortressam.ai/excel-addin/src/taskpane/index.html`?
- Is the path deviation acceptable? The deployment is `cp -r dist/* wwwroot/excel-addin/` → results in `/excel-addin/src/taskpane/index.html`. This is a valid path but longer than spec expected. Evaluate: is there a simpler way to get `dist/taskpane/index.html` that doesn't break the commands.html entry? Or should we accept the longer path?

**Make explicit ACCEPTED or REJECTED decision with reasoning.**

### CHECK 2 — manifest.xml Critical URLs (HIGH)

Read `manifest.xml` and report the EXACT values of:
- `<SourceLocation DefaultValue="...">` (line ~28)
- `<bt:Url id="Taskpane.Url" DefaultValue="...">` (line ~80)

Both must be `https://fait.dev.fortressam.ai/excel-addin/src/taskpane/index.html`.
If either is still the old bare directory URL, verdict is FAIL.

### CHECK 3 — @microsoft/office-js Removal (HIGH)

Read `package.json` in full. Verify:
- `@microsoft/office-js` does NOT appear in `dependencies`
- `@microsoft/office-js` does NOT appear in `devDependencies`
- Also scan all source files: grep result from `grep -r "@microsoft/office-js" src/` showed no matches

Report actual package.json deps vs devDeps structure.

### CHECK 4 — vite.config.ts format override (HIGH)

Read `vite.config.ts`. Verify:
- No `output.format:` setting anywhere in rollupOptions
- Specifically no `'iife'` or `'cjs'` format value
- Report the full rollupOptions block as found

### CHECK 5 — storage.ts fix (MEDIUM)

Read `src/taskpane/services/storage.ts`. Verify:
- Uses `localStorageShim` + `getStorage()` pattern (same as settings.ts)
- No bare `OfficeRuntime.storage.*` calls remaining (only `(window as any).OfficeRuntime?.storage` in getStorage is fine)
- grep shows: line 1 comment, line 13 `(window as any).OfficeRuntime?.storage ?? localStorageShim` — this is correct pattern

### CHECK 6 — manifest.local.xml AppDomains (MEDIUM)

Read `manifest.local.xml`. Verify:
- `<AppDomains>` contains `<AppDomain>https://localhost:3000</AppDomain>`
- `<SourceLocation>` is `https://localhost:3000/src/taskpane/index.html`
- `<bt:Url id="Taskpane.Url">` is `https://localhost:3000/src/taskpane/index.html`
- `<bt:Url id="Commands.Url">` — **FLAG THIS:** Tony set it to `https://localhost:3000/public/commands.html`

  **IMPORTANT CHECK on Commands.Url in manifest.local.xml:**
  Vite's default `publicDir` is `public/`. Files in `public/` are served at the ROOT path, NOT at `/public/`. So during `npm run dev`, `public/commands.html` is served at `https://localhost:3000/commands.html` — NOT at `https://localhost:3000/public/commands.html`.
  
  If `Commands.Url` is `https://localhost:3000/public/commands.html`, Office will get a 404 when loading the ribbon commands handler. Is this a bug?

  Evaluate: Does `manifest.local.xml` have `https://localhost:3000/public/commands.html` or `https://localhost:3000/commands.html`? If it's `/public/commands.html`, that is an **Important bug** — the commands page will 404 in local dev.

### CHECK 7 — dist/src/taskpane/index.html contents (LOW)

Read `dist/src/taskpane/index.html`. Verify:
- Contains `<script src="https://appsforoffice.microsoft.com/lib/1/hosted/office.js"...>` in `<head>`
- Contains `<script type="module" crossorigin src="...">` referencing a hashed `.js` bundle
- Report the exact script tag content

### CHECK 8 — Consistency: spec vs implementation

The spec (Reed Richards) specified `dist/taskpane/index.html` but Tony produced `dist/src/taskpane/index.html`. Review:
1. Is the vite.config.ts input correct? (`src/taskpane/index.html` as input → rolldown produces `dist/src/taskpane/index.html`)
2. Could Tony have used `rollupOptions.output.entryFileNames` or similar to flatten the output path while keeping the `public/commands.html` entry?
3. Is there a better approach that avoids the deep nested path in manifest URLs?

Note: Reed's spec comment says "When Vite's input is `src/taskpane/index.html`, it preserves the relative path structure in output. The built HTML will be at `dist/taskpane/index.html`" — this appears to be incorrect for Vite v8/rolldown. Reed anticipated `dist/taskpane/` but the actual output is `dist/src/taskpane/`. Tony's acceptance of the longer path may be pragmatically correct given the rolldown constraint.

---

## Report Format

Provide a structured report covering:

1. **Path Deviation Decision** — ACCEPTED or REJECTED, with explicit reasoning
2. **manifest.xml URLs** — exact values found, PASS/FAIL
3. **@microsoft/office-js** — PASS/FAIL
4. **vite.config.ts format** — exact rollupOptions block, PASS/FAIL
5. **storage.ts fix** — PASS/FAIL
6. **manifest.local.xml AppDomains** — PASS/FAIL, flag Commands.Url issue if present
7. **dist/src/taskpane/index.html** — exact script tags found, PASS/FAIL
8. **Issues Found** — categorized as Critical / Important / Nitpick
9. **Overall Verdict** — PASS / NEEDS-CHANGES / FAIL

Be specific. Quote exact file content where relevant. Don't be lenient on Important issues.
