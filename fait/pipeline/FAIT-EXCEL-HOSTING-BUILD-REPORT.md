# Build Report: FAIT-EXCEL-HOSTING

**Task:** Host Excel add-in static files in FAIT at `/excel-addin/`
**Agent:** Tony Stark (software-engineer)
**Date:** 2026-03-14
**Status:** ✅ COMPLETE

---

## Approach Used

**Committed dist directly into wwwroot** (not buildspec clone approach).

Rationale: The `buildspec.yml` has no GitHub authentication mechanism for private repos — it only handles ECR login and a single `docker build` command. The `fwhite13/fait-for-excel` repo is private, so CodeBuild would have no access without additional credentials setup. Committing the built assets directly into the FAIT repo is simpler, has no external dependency, and is consistent with how other static assets (`wwwroot/css`, `wwwroot/js`, `wwwroot/images`) are already handled.

---

## Verification Checklist

### ✅ `base: '/excel-addin/'` confirmed in vite-built assets

`vite.config.ts` confirmed:
```typescript
base: '/excel-addin/',
```

Built `index.html` asset references verified:
```html
<script type="module" crossorigin src="/excel-addin/assets/taskpane-DXM0QfXm.js"></script>
<link rel="stylesheet" crossorigin href="/excel-addin/assets/taskpane-DarIh3SN.css">
```
All asset paths correctly prefixed with `/excel-addin/`.

### ✅ `app.UseStaticFiles()` confirmed in Program.cs

Located at line ~260 in `src/FortressAI.Web/Program.cs`:
```csharp
app.UseStaticFiles();
```
No additional configuration needed — ASP.NET Core serves `wwwroot/` by default with `UseStaticFiles()`.

### ✅ Files committed to FAIT repo

8 files added to `src/FortressAI.Web/wwwroot/excel-addin/`:

| File | Purpose |
|------|---------|
| `index.html` | Taskpane entry point (copied from `dist/src/taskpane/index.html` to root) |
| `commands.html` | Minimal Office.js stub (required by manifest `FunctionFile`) |
| `assets/taskpane-DXM0QfXm.js` | Vite-built JS bundle |
| `assets/taskpane-DarIh3SN.css` | Vite-built CSS bundle |
| `assets/icon-16.png` | Add-in icon (16×16) |
| `assets/icon-32.png` | Add-in icon (32×32) |
| `assets/icon-80.png` | Add-in icon (80×80) |
| `src/taskpane/index.html` | Secondary path (from Vite's nested output structure) |

### ✅ FAIT commit pushed

- **Commit SHA:** `cd22e51`
- **Branch:** `main`
- **Remote:** `github.com:fwhite13/fortress-intelligence-platform.git`
- **Message:** `feat(excel-addin): serve built add-in static files at /excel-addin/`

### ✅ dotnet build: 0 errors

```
29 Warning(s)
0 Error(s)
Time Elapsed 00:00:05.92
```
Warnings are pre-existing MudBlazor analyzer warnings, unrelated to this change.

---

## Manifest Status

`~/projects/fait-for-excel/manifest.xml` already correctly configured:
- `<SourceLocation>`: `https://fait.dev.fortressam.ai/excel-addin/` ✅
- `<IconUrl>`: `https://fait.dev.fortressam.ai/excel-addin/assets/icon-32.png` ✅
- `Icon.16x16`: `https://fait.dev.fortressam.ai/excel-addin/assets/icon-16.png` ✅
- `Icon.32x32`: `https://fait.dev.fortressam.ai/excel-addin/assets/icon-32.png` ✅
- `Icon.80x80`: `https://fait.dev.fortressam.ai/excel-addin/assets/icon-80.png` ✅
- `Commands.Url`: `https://fait.dev.fortressam.ai/excel-addin/commands.html` ✅
- `Taskpane.Url`: `https://fait.dev.fortressam.ai/excel-addin/` ✅

No manifest changes needed.

---

## Build Notes

### Vite Output Structure Fix
Vite's `rollupOptions.input` preserved the source path, outputting the HTML to `dist/src/taskpane/index.html` rather than `dist/index.html`. This would have placed the taskpane at `/excel-addin/src/taskpane/` instead of `/excel-addin/`.

**Fix applied:** Copied `dist/src/taskpane/index.html` → `dist/index.html` before copying into wwwroot. The nested path is also included (served at the nested URL, harmless).

### Icon Files
Icons (`icon-16.png`, `icon-32.png`, `icon-80.png`) were in `~/projects/fait-for-excel/assets/` (source assets, not included in Vite output). Copied manually into `wwwroot/excel-addin/assets/` to match manifest URL references.

### `.gitignore` Check
No `wwwroot` or `wwwroot/excel-addin` entries in `.gitignore`. All 8 files were cleanly staged and committed.

---

## Expected Serving Behaviour Post-Deploy

Once FAIT redeploys via CodeBuild:

| URL | File Served |
|-----|------------|
| `https://fait.dev.fortressam.ai/excel-addin/` | `wwwroot/excel-addin/index.html` |
| `https://fait.dev.fortressam.ai/excel-addin/commands.html` | `wwwroot/excel-addin/commands.html` |
| `https://fait.dev.fortressam.ai/excel-addin/assets/taskpane-DXM0QfXm.js` | Vite JS bundle |
| `https://fait.dev.fortressam.ai/excel-addin/assets/taskpane-DarIh3SN.css` | Vite CSS bundle |
| `https://fait.dev.fortressam.ai/excel-addin/assets/icon-*.png` | Add-in icons |
