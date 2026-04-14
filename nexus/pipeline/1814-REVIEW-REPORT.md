# Review Report — ADO #1814 — FileType.Text + Spec Gen Routing Fix

**Reviewer:** Hawkeye (code-reviewer)
**Cycle:** 1
**Commit:** `22d1dd2` (on top of `9c4eaeb` / #1813)
**Date:** 2026-04-13

---

## Verdict: NEEDS-CHANGES

---

## Spec Compliance Check

### Files Modified in Commit `22d1dd2`
Per `git show --stat 22d1dd2`:
- `Services/SpecGenerationService.cs` — ✅ modified (only file in this commit)

### Note on Commit Attribution
The #1814 commit message claims credit for changes that were actually made in #1813 (`9c4eaeb`):
- `FileType.cs` enum update — done in `9c4eaeb`
- `DetectFileType` MIME routing — done in `9c4eaeb`
- `UploadAsync` UTF-8 decode — done in `9c4eaeb`

The actual `22d1dd2` commit only adds the `SpecGenerationService` switch cases. History is misleading but the combined state (both commits) is what's being reviewed.

### §7 Acceptance Criteria
- ✅ `FileType.Text` enum value exists (from #1813)
- ✅ MIME routing for `text/plain`, `text/markdown`, `text/x-markdown`, `application/json` → `FileType.Text` (from #1813)
- ✅ UTF-8 decode for `Text` and `Other` (from #1813)
- ✅ `BuildPromptAsync` `FileType.Text` case with `**File Contents:**` header
- ✅ `BuildPromptAsync` `FileType.Pdf` case independent of `Other`
- ❌ `DiscoveryService.GenerateQuestionsAsync` — `FileType.Text` case NOT added (blocks PASS)

**Spec compliance verdict: ❌ NON-COMPLIANT** — `DiscoveryService.cs` was not updated; text files produce wrong discovery prompts.

---

## Consistency Audit

**Files Cross-Referenced:**
- `FileType.cs` ↔ `FileStorageService.cs` ↔ `SpecGenerationService.cs` — ✅ All enum values used consistently
- `FileType.cs` ↔ `DiscoveryService.cs` — ❌ `DiscoveryService` has no `case FileType.Text:` — see Critical #1

**Undocumented Dependencies Found:**
- `DiscoveryService.cs` contains a `switch (file.FileType)` block (lines ~291–309) with a comment `// FileType.Text added in #1814` — the comment is inaccurate; the case was never added

---

## Critical Issues [1]

### C1: `DiscoveryService.cs` — `FileType.Text` never added to switch
- **File:** `Services/Discovery/DiscoveryService.cs` (lines ~291–309)
- **Category:** Consistency / Correctness
- **Issue:** `GenerateQuestionsAsync` has a switch on `file.FileType`. The inline comment claims `FileType.Text` was added in #1814, but it was not. `FileType.Text` files fall to the `default:` arm which emits `*[Binary or unsupported file type]*` — silently dropping all text file content from discovery question generation.
- **Evidence:**
  ```csharp
  switch (file.FileType)
  {
      case FileType.Html:
      case FileType.Pdf:
      case FileType.Other: // FileType.Text added in #1814  ← LIE
          if (!string.IsNullOrWhiteSpace(file.ProcessedText))
          {
              var content = file.ProcessedText.Length > 2000
                  ? file.ProcessedText[..2000] + "\n... [truncated]"
                  : file.ProcessedText;
              userPromptSb.AppendLine("**Contents:**");
              userPromptSb.AppendLine(content);
          }
          else { userPromptSb.AppendLine("*[File content not available]*"); }
          break;

      case FileType.Image:
          userPromptSb.AppendLine("*[Image file — visual content not included in question generation]*");
          break;

      default:
          userPromptSb.AppendLine("*[Binary or unsupported file type]*");  ← Text files land here
          break;
  }
  ```
- **Impact:** Any `.txt`, `.md`, or `.json` file uploaded to a project has its content silently dropped during discovery. Discovery questions are generated without the file content. This is a silent data loss bug — no error, just wrong behavior.
- **Fix:**
  ```diff
  - case FileType.Other: // FileType.Text added in #1814
  + case FileType.Text:
  + case FileType.Other:
  ```
  Remove the misleading comment. Add `case FileType.Text:` before `case FileType.Other:` in the same fall-through group.

---

## Important Issues [1]

### I1: `SpecGenerationService.cs` — `FileType.Other` case still emits `ProcessedText`
- **File:** `Services/SpecGenerationService.cs` (BuildPromptAsync `Other` case)
- **Category:** Design intent mismatch
- **Issue:** The design intent per the brief is that `Other` = truly unknown/binary, no content. But the `Other` case still conditionally emits `ProcessedText` if present. Currently inert because no MIME type reachable via the upload allowlist produces `FileType.Other` with text content. However, if `AllowedTypes` is ever expanded, `Other` would unexpectedly include content.
- **Evidence:**
  ```csharp
  case FileType.Other:
  default:
      sb.AppendLine("**File Type: Other**");
      if (!string.IsNullOrWhiteSpace(file.ProcessedText))
          sb.AppendLine(file.ProcessedText);  // ← should not be here per design
      else
          sb.AppendLine("*No text content available for this file.*");
      break;
  ```
- **Fix:** Replace content block with a static skip message:
  ```diff
  - if (!string.IsNullOrWhiteSpace(file.ProcessedText))
  -     sb.AppendLine(file.ProcessedText);
  - else
  -     sb.AppendLine("*No text content available for this file.*");
  + sb.AppendLine("*Binary or unsupported file — content skipped.*");
  ```
- **Blocking?** No — deferrable. Recommend fixing now while the switch is already being touched.

---

## Nitpicks [1]

- **N1:** Commit `22d1dd2` message inaccurately claims work done in #1813. Amend or note in follow-up. Not blocking but corrupts `git log`.

---

## Positive Observations

- MIME routing in `DetectFileType` is clean and correct. `text/html` correctly resolves before the `text/*` group — no leakage into `FileType.Text`. The `ToLowerInvariant()` normalization is good defensive practice.
- `BuildPromptAsync` `FileType.Text` and `FileType.Pdf` cases both have `IsNullOrWhiteSpace` guards with fallback messages — correct null safety.
- Enum definition is clean with exactly one `Text` value, correct implicit ordering.

---

## Acceptance Criteria Verification

| # | Criterion | Status |
|---|-----------|--------|
| 1 | `FileType.Text` enum exists, single definition | ✅ Verified — exactly one `Text` value |
| 2 | MIME routing correct and exhaustive | ✅ Verified — all 8 MIME paths correct |
| 3 | UTF-8 decode fires for `Text` and `Other` | ✅ Verified — `|| fileType == FileType.Other` condition covers both |
| 4 | `BuildPromptAsync` `Text` case uses `ProcessedText` | ✅ Verified — null guard + `**File Contents:**` header |
| 5 | `FileType.Pdf` independent in `BuildPromptAsync` | ✅ Verified — explicit `break`, no fall-through |
| 6 | `DiscoveryService` updated for `FileType.Text` | ❌ NOT met — critical bug, blocks PASS |

---

## What Tony Needs to Fix

**Required (blocks merge):**

1. **`Services/Discovery/DiscoveryService.cs`** — In `GenerateQuestionsAsync`, find the switch on `file.FileType`. Add `case FileType.Text:` before `case FileType.Other:` in the fall-through group that emits `**Contents:**`. Remove the misleading comment. Single line change + removing a comment.

**Recommended (same pass):**

2. **`Services/SpecGenerationService.cs`** — Replace the `Other` case body with a static "binary/unsupported — content skipped" message. Drop the `ProcessedText` conditional.

---

_Reviewed by Hawkeye — cycle 1 — 2026-04-13_
