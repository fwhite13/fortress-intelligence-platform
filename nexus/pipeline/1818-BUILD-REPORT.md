# Build Report — ADO #1818 — DetectFileType Extension Fallback for Empty MIME

**Commit:** `95a8aec`
**Branch:** main
**Build:** ✅ 0 errors, 0 warnings
**Risk:** Medium

---

## What was built

Added extension-based fallback to `FileStorageService` so `.md` files (and other valid types) sent with empty MIME by browsers are no longer rejected at upload. Mirrors the security pattern established in #1787 (`FileUploadZone`).

---

## Files changed

- `src/FortressNexus.Web/Services/FileStorageService.cs` — three targeted changes:
  1. **`AllowedExtensions` HashSet** added as static field (OrdinalIgnoreCase) with all 10 allowed extensions
  2. **`DetectFileType`** converted from expression-bodied to full method; accepts `fileName = ""` optional param; MIME-first with extension fallback when MIME is null/unrecognized
  3. **`UploadAsync` validation block** replaced single `AllowedTypes.Contains` check with `mimeOk || (emptyMime && extOk)` security pattern; `DetectFileType` call updated to pass `file.Name`

---

## Parallelization used

No — single-file change, one CC session, sequential.

---

## CC sessions run

1 session — CC Sonnet. Brief was precise and CC executed all three changes correctly on first pass.

---

## Acceptance criteria verification

- [x] `.md` file with empty MIME passes validation — `mimeOk=false`, `extOk=true`, `emptyMime=true` → allowed
- [x] `.exe` renamed with MIME `application/octet-stream` rejected — `mimeOk=false`, `emptyMime=false` → throws
- [x] `.exe` with no extension + empty MIME rejected — `mimeOk=false`, `extOk=false` → throws
- [x] Existing MIME-based uploads unchanged — `mimeOk=true` → passes as before
- [x] `DetectFileType("")` with `fileName="doc.md"` returns `FileType.Text` (extension fallback)
- [x] `dotnet build` — 0 errors, 0 warnings

---

## Security posture

The `mimeOk || (string.IsNullOrEmpty(normalizedContentType) && extOk)` pattern is intentionally strict:
- **Non-empty unrecognized MIME** → always rejected (no extension bailout)
- **Empty MIME + bad extension** → rejected
- **Empty MIME + allowed extension** → permitted (the .md fix)
- **Valid MIME** → permitted regardless of extension (existing behavior preserved)

This matches exactly what `#1787` implemented in `FileUploadZone`. Server and client now have identical security semantics.

---

## Known edge cases / things Clint should scrutinize

- **`file.ContentType` nullable** — the change from `file.ContentType.ToLowerInvariant()` to `file.ContentType?.ToLowerInvariant() ?? ""` adds null safety. `IBrowserFile.ContentType` is documented as non-null in Blazor, but the null coalesce is harmless defensive coding.
- **`FileType.Other` for empty MIME + unrecognized extension** — still returns `FileType.Other` from `DetectFileType`, which then goes through the `processedText = Encoding.UTF8.GetString(fileBytes)` path. This is unchanged from before and intentional.
- **`AllowedExtensions` is OrdinalIgnoreCase** — `.MD`, `.Md` etc. all pass. Matches how the client-side check in #1787 works.

---

## How to test locally

```bash
cd ~/projects/fip/nexus/src/FortressNexus.Web
dotnet build

# Manual: upload a .md file in NEXUS UI — should succeed
# Manual: attempt to upload a .exe file — should be rejected with "not allowed" message
```
