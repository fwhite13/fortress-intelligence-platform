# Review Report — ADO #1705
## File upload allowlist (.md/.json/.txt) — Commit cb0b412

**Reviewed by:** Hawkeye (code-reviewer)  
**Date:** 2026-04-13  
**Cycle:** 1  
**Risk:** Low (per brief) → **Elevated** (silent functional failure found)

---

### Verdict: NEEDS-CHANGES

---

## Spec Compliance Check

**Files changed per commit:**
- `Components/Shared/FileUploadZone.razor` ✅ modified
- `Services/FileStorageService.cs` ✅ modified

**Scope:** ✅ Clean — only the two specified files changed.  
**Out-of-scope changes:** None detected.

**Spec compliance verdict:** ✅ COMPLIANT — correct files, correct scope.

---

## CC Review Summary

Claude Code (sonnet) was run against both changed files plus downstream consumers (`SpecGenerationService.cs`, `SubmissionDetail.razor`, `FileType.cs`). 

CC confirmed:
- ✅ MIME parity between client and server — perfect 1:1 match
- ✅ `text/x-markdown` present in both files
- ✅ Accept attr extensions complete
- ✅ `DataObject` icon confirmed in MudBlazor 7.16.0 dll (via `strings`)
- ✅ `FileType.Other` has no downstream throw — both consumers handle it safely
- ❌ **CRITICAL:** `DetectFileType` not updated — new types fall to `FileType.Other` with no extraction → `ProcessedText = null` → spec generation silently outputs placeholder

---

## Consistency Audit

**MIME arrays side by side:**

| # | MIME Type | `AcceptedTypes` (Razor) | `AllowedTypes` (Service) |
|---|-----------|------------------------|--------------------------|
| 1 | text/html | ✅ | ✅ |
| 2 | image/png | ✅ | ✅ |
| 3 | image/jpeg | ✅ | ✅ |
| 4 | image/jpg | ✅ | ✅ |
| 5 | image/webp | ✅ | ✅ |
| 6 | application/pdf | ✅ | ✅ |
| 7 | text/markdown | ✅ | ✅ |
| 8 | text/x-markdown | ✅ | ✅ |
| 9 | application/json | ✅ | ✅ |
| 10 | text/plain | ✅ | ✅ |

**Result: MATCH — no parity gap.** This is not the problem.

**`Accept` attribute:** `.html,.png,.jpg,.jpeg,.webp,.pdf,.md,.json,.txt` ✅

---

## Critical Issues [1]

### C1 — `DetectFileType` not updated: new types produce null `ProcessedText`

- **File:** `Services/FileStorageService.cs` (lines 31–38 + 80–97)
- **Category:** correctness / spec non-compliance
- **Issue:** `DetectFileType` has no cases for the 4 new MIME types — they all fall to `FileType.Other`. The `UploadAsync` extraction block has branches for `Html` and `Pdf` only; `FileType.Other` has no branch, so `processedText` remains `null`. The `UploadAsync` completes successfully, file lands in S3, no error is raised — but `ProcessedText = null` on the `UploadedFile` entity.

**Call chain:**
```
text/markdown → DetectFileType → FileType.Other
                                    ↓
                          UploadAsync: no extraction branch
                                    ↓
                          processedText = null
                                    ↓
                          SpecGenerationService case FileType.Other:
                             if (!string.IsNullOrWhiteSpace(file.ProcessedText))  // FALSE
                                 sb.AppendLine(file.ProcessedText);
                             else
                                 sb.AppendLine("*No text content available for this file.*");
                                 // ← always hit for every .md/.json/.txt upload
```

- **Impact:** Upload succeeds silently. Users can upload .md/.json/.txt files. They appear in the UI. But spec generation never sees their content — only a `*No text content available*` placeholder. The feature is functionally broken.

**Fix (two options — choose one):**

**Option A (minimal — single branch, no enum change):** Add an `Other` extraction branch in `UploadAsync`:
```csharp
else if (fileType == FileType.Other)
{
    // Plain text types (.md, .json, .txt) — read as UTF-8
    processedText = Encoding.UTF8.GetString(fileBytes);
}
```
This is lowest-risk — no enum changes, no migration, no downstream switch updates. Works correctly because `SpecGenerationService` already handles `FileType.Other` and outputs `processedText` if non-null.

**Option B (explicit — adds enum values):** Add `FileType.Markdown`, `FileType.Json`, `FileType.PlainText` to the enum, update `DetectFileType`, add extraction branches, and update all downstream switches. Higher fidelity, but touches more files — higher risk for a low-risk ticket.

**Recommendation: Option A.** One-line fix, zero migration risk, fully functional.

---

## Important Issues [1]

### I1 — `text/plain` scope is overbroad

- **File:** Both files (AllowedTypes / AcceptedTypes + Accept attr)
- **Category:** scope concern
- **Issue:** Browsers report `.csv`, `.log`, `.sh`, `.py`, `.env`, `.conf` as `text/plain`. All of these pass client and server validation without error. In the context of a compliance/insurance spec generation platform, this means users can inadvertently upload shell scripts, environment files, or log files — they upload cleanly and appear valid.
- **Not a security exploit** in the current architecture (content goes to S3 and LLM as text). But it's an unintended scope expansion worth the team explicitly accepting.
- **Recommendation:** Document conscious acceptance of `text/plain` breadth, or constrain to extension-based checking if .txt-only was the intent. Not a hard block.

---

## Nitpicks [0]

None. Code style, hint formatting, and switch pattern are clean.

---

## Positive Observations

- ✅ MIME parity between client and server is exact — good discipline.
- ✅ `text/x-markdown` browser-compat variant correctly included in both files.
- ✅ Switch pattern in `GetFileIcon` uses `or` patterns cleanly — idiomatic C# 9.
- ✅ `FileType.Other` fallback in both `SpecGenerationService` and `SubmissionDetail` is safe (no throw, graceful placeholder/icon).

---

## What Tony Needs to Fix

**C1 fix (FileStorageService.cs):**

In `UploadAsync`, after the PDF extraction block (around line 96), add:

```csharp
else if (fileType == FileType.Other)
{
    // Plain text types (.md, .json, .txt) — store raw UTF-8 content for spec generation
    processedText = Encoding.UTF8.GetString(fileBytes);
}
```

That's it. `Encoding.UTF8` is already imported. No enum changes, no migration, no downstream updates needed. Re-test: upload a .md file → confirm `ProcessedText` is non-null in DB → confirm spec generation outputs content instead of placeholder.

---

## Acceptance Criteria Verification

Per the task brief, the AC is: .md/.json/.txt are accepted by the upload UI and server.

- ✅ UI accepts new extensions (Accept attr)
- ✅ Client MIME validation accepts new types
- ✅ Server MIME validation accepts new types
- ❌ **Files are not actually usable in spec generation** — `ProcessedText` is always null for new types

AC is technically ~80% met. Uploads work. Content processing is broken.

---

_Reviewed with Claude Code (sonnet) adversarial spec. Hawkeye — cycle 1._

---

## Cycle 2 — ADO #1705

**Commit:** `4692142`  
**Reviewed by:** Hawkeye (code-reviewer)  
**Date:** 2026-04-13  
**Cycle:** 2 (targeted — one fix from C1 C1 finding)  
**Risk:** Low

---

### Verdict: ✅ PASS

---

### What Tony Fixed

Added `else if (fileType == FileType.Other) { processedText = Encoding.UTF8.GetString(fileBytes); }` in `UploadAsync` after the Pdf extraction branch. This is the exact Option A fix recommended in Cycle 1.

---

### CC Review Summary

Claude Code (sonnet) ran 5 targeted checks against `FileStorageService.cs`. All 5 passed.

---

### Targeted Checks

| # | Check | Result |
|---|-------|--------|
| 1 | Branch placement (Html → Pdf → Other → Image comment) | ✅ PASS |
| 2 | `Encoding.UTF8.GetString(fileBytes)` correct + null-safe | ✅ PASS |
| 3 | No regression to Html/Pdf branches | ✅ PASS |
| 4 | Scope clean — exactly 4 lines added, nothing else | ✅ PASS |
| 5 | `Encoding` already imported (no new using needed) | ✅ PASS |

---

### Critical Issues [0]

None. C1 from Cycle 1 is resolved.

---

### Scope

Only `Services/FileStorageService.cs` changed. Verified by `git show 4692142 --stat`: 1 file, 4 insertions, 0 deletions elsewhere.

---

### Acceptance Criteria — Final

- ✅ .md/.json/.txt accepted by upload UI (from C1)
- ✅ .md/.json/.txt accepted by server MIME validation (from C1)
- ✅ `ProcessedText` populated for FileType.Other uploads (fixed in C2)
- ✅ Spec generation will output actual file content for new types

All AC met. Feature is fully functional.

---

_Reviewed with Claude Code (sonnet) adversarial spec. Hawkeye — cycle 2._
