# Build Report — ADO #1787

## What was built
MIME-or-extension fallback in `FileUploadZone.razor`. Browsers report empty `ContentType` for `.md` and `.json` files; the old check rejected them. The fix accepts a file if either its MIME type or its file extension is in the allowlist.

## Files changed
- `src/FortressNexus.Web/Components/Shared/FileUploadZone.razor`
  - Added `_allowedExtensions` static `HashSet<string>` (9 extensions: .md, .json, .txt, .pdf, .html, .png, .jpg, .jpeg, .webp)
  - Replaced single MIME check with `mimeOk || extOk` — falls back to extension when ContentType is empty/null

## AcceptedTypes default
Already included `"text/markdown"`, `"text/x-markdown"`, `"application/json"`, `"text/plain"` — no change needed.

## Parallelization used
No — single-file, single CC session.

## CC sessions run
1 — CC Sonnet, piped brief, one-shot.

## Build result
```
Build succeeded. 0 errors, 0 warnings
```

## Commit
`3faa75c` — `fix(nexus#1787): MIME empty string fallback to extension check in FileUploadZone`  
Branch: `origin/main`

## Acceptance criteria verification
- [x] `FileUploadZone.razor` — extension fallback logic in place — verified by reading file post-CC
- [x] `dotnet build` — 0 errors, 0 warnings — confirmed in CC output
- [x] Build Report — this file
- [x] ADO comment — posted

## Things Clint should scrutinize
- `Path.GetExtension()` returns `""` for files with no extension — the `extOk` check on that will correctly be `false`, so no accidental pass-through.
- `_allowedExtensions` uses `StringComparer.OrdinalIgnoreCase` — `.MD`, `.Json`, etc. will match correctly.
- The old error message included the raw MIME type (useful for debugging); new message lists allowed types instead (better UX). If Clint prefers the old debug info, easy to restore.

## How to test locally
1. `cd ~/projects/fip/nexus && dotnet run --project src/FortressNexus.Web`
2. Upload a `.md` file — should be accepted
3. Upload a `.json` file — should be accepted
4. Upload a `.exe` file — should be rejected with the new error message

---

## Cycle 2 — Validation Logic Fix

### What was fixed
Security gap: `!mimeOk && !extOk` allowed files with a non-allowlisted MIME type (e.g. `application/octet-stream`) but a matching extension to pass validation. Extension fallback now only fires when MIME is empty/null.

Also removed non-standard `"image/jpg"` from `AcceptedTypes` default — browsers always report JPEG as `"image/jpeg"`.

### Files changed
- `src/FortressNexus.Web/Components/Shared/FileUploadZone.razor`
  - **C1:** Replaced `if (!mimeOk && !extOk)` with `var valid = mimeOk || (string.IsNullOrEmpty(mime) && extOk); if (!valid)` — extension fallback now gated on MIME being empty/null
  - **I1:** Removed `"image/jpg"` from `AcceptedTypes` parameter default array

### Build result
```
Build succeeded. 0 errors, 0 warnings.
```

### Commit
`98c1500` — `fix(nexus#1787): extension fallback only when MIME empty; remove non-standard image/jpg`
Branch: `origin/main`

### Acceptance criteria
- [x] C1 logic fix applied and verified by file grep
- [x] I1 `"image/jpg"` removed from AcceptedTypes default
- [x] `dotnet build` — 0 errors
- [x] Cycle 2 section appended to build report
- [x] ADO comment posted

### Security note for Clint
Old behavior: file with MIME=`application/octet-stream` + ext=`.md` → PASSES (both checks fail → `!false && !false` → condition fires but then inverted → actually wait, old `if (!mimeOk && !extOk)` means "reject if BOTH fail" — so a file where only one check fails PASSES). New behavior: a file must either (a) have a valid MIME or (b) have NO MIME and a valid extension. A file with a wrong MIME type will now always be rejected regardless of extension.
