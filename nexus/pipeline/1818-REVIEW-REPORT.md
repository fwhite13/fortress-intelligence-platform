# Review Report — ADO #1818 — DetectFileType Extension Fallback

**Verdict: PASS**
**Cycle:** 1
**Reviewer:** Hawkeye (code-reviewer)
**Commit:** `95a8aec`
**File:** `Services/FileStorageService.cs`
**Date:** 2026-04-13

---

## CC Review Summary

Claude Code performed a 10-point adversarial review against the full source at commit `95a8aec`. All 6 primary (Critical/Important) checks passed. Two notes were flagged as cleanup candidates — neither is a functional bug or security issue.

---

## Spec Compliance Check

**§2 Codebase Map:**
- `Services/FileStorageService.cs` — ✅ modified as specified (single file, 35 insertions / 7 deletions)

**§6 Out of Scope:**
- ✅ No out-of-scope changes detected. One file touched, matching the build report.

**§7 Acceptance Criteria:**
- [x] `AllowedExtensions` HashSet added with `OrdinalIgnoreCase` — ✅ Verified
- [x] `DetectFileType(contentType, fileName)` overload with extension fallback — ✅ Verified
- [x] `UploadAsync` validation uses `mimeOk || (IsNullOrEmpty(ct) && extOk)` pattern — ✅ Verified
- [x] `.md` → `FileType.Text` (core fix) — ✅ Verified

**Spec compliance verdict:** ✅ COMPLIANT

---

## Consistency Audit

**Files Cross-Referenced:**
- `AllowedTypes` (array) ↔ `AllowedExtensions` (HashSet) — ✅ fully symmetric coverage
- `DetectFileType` extension switch ↔ `AllowedExtensions` — ✅ aligned, with one noted dead branch (`.gif`)
- `UploadAsync` validation gate ↔ `DetectFileType` call site — ✅ consistent, `file.Name` passed correctly

**Undocumented Dependencies:** None found. Single-file change.

---

## Critical Issues — 0

---

## Important Issues — 0

---

## Nitpicks — 2

**N1: Dead `.gif` branch in `DetectFileType` extension switch** (`FileStorageService.cs`, extension switch)
`.gif` → `FileType.Image` is unreachable: `.gif` is absent from `AllowedExtensions`, so any file with empty MIME and `.gif` extension is rejected by `UploadAsync` before `DetectFileType` is called. Not a bug. Cleanup candidate if desired.

**N2: `FileType.Other` arm in text extraction block**
```csharp
else if (fileType == FileType.Text || fileType == FileType.Other)
```
`FileType.Other` cannot reach this branch in practice — files producing it are blocked by the validation gate. The `|| FileType.Other` clause is dead. Low-risk. Could be removed for defensive tightness.

---

## Positive Observations

- **Security gate is correct and tight.** The De Morgan equivalence of `if (!mimeOk && !(IsNullOrEmpty(ct) && extOk))` correctly implements `mimeOk || (IsNullOrEmpty && extOk)` — the "non-empty bad MIME still blocked" invariant is preserved. A `.txt`-renamed `.exe` with MIME `application/x-executable` is rejected.
- **Null safety is solid.** `file.ContentType?.ToLowerInvariant() ?? ""` handles null MIME cleanly; `Path.GetExtension("")` degrades gracefully to `""`.
- **Extension fallback is MIME-primary.** Known MIMEs short-circuit before the extension switch — the fallback is truly additive, not a parallel track that could be exploited.
- **Core fix verified.** `.md` → `FileType.Text`. The `.md` upload failure that triggered this ticket is resolved.
- **Comment quality.** The security rule inline comment (`// Security rule: MIME must be valid OR (MIME is empty/missing AND extension is allowed)`) clearly documents intent.

---

## Acceptance Criteria Verification

| # | Criterion | Status |
|---|-----------|--------|
| 1 | `AllowedExtensions` HashSet with `OrdinalIgnoreCase` | ✅ Present — line ~21 |
| 2 | `DetectFileType` accepts `fileName` param, extension fallback when MIME empty/unrecognized | ✅ Present — extension switch only runs when `mimeType` is null |
| 3 | `mimeOk \|\| (IsNullOrEmpty(ct) && extOk)` in `UploadAsync` | ✅ Present — verified De Morgan equivalence |
| 4 | `.md` → `FileType.Text` | ✅ Present — `".md" or ".txt" or ".json" => FileType.Text` |
| 5 | Null-safe `file.ContentType` handling | ✅ `?.ToLowerInvariant() ?? ""` |
| 6 | `DetectFileType` called with `file.Name` | ✅ `DetectFileType(normalizedContentType, file.Name)` |

---

## Final Verdict: PASS ✅

Ships. Code is correct, secure, and solves the stated problem. Two nitpick cleanup items noted but non-blocking.
