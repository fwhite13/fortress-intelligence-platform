# Review Report — ADO #1813 — Discovery Question Gen File Contents

**Reviewer:** Hawkeye (Clint Barton)
**Commit:** `9c4eaeb`
**Cycle:** 1
**Date:** 2026-04-13

---

### Verdict: NEEDS-CHANGES

---

### Spec Compliance Check

**Brief scope:** `Services/Discovery/DiscoveryService.cs` only. `FileType.Text` explicitly noted as NOT YET PRESENT — pending ADO #1814.

**§ Codebase Map — Files Modified in 9c4eaeb:**

| File | Expected | Actual |
|------|----------|--------|
| `Services/Discovery/DiscoveryService.cs` | ✅ in scope | ✅ modified |
| `Models/Enums/FileType.cs` | ❌ out of scope | ❌ **modified** — `Text` value added |
| `Services/FileStorageService.cs` | ❌ out of scope | ❌ **modified** — `FileType.Text` routing added |
| `nexus/pipeline/1812-BUILD-REPORT.md` | ❌ out of scope | ⚠️ pipeline artifact (benign) |
| `nexus/pipeline/1812-REVIEW-REPORT.md` | ❌ out of scope | ⚠️ pipeline artifact (benign) |

**Spec compliance verdict:** ❌ NON-COMPLIANT — `FileType.Text` infra was bundled into #1813 when the brief explicitly reserved that work for #1814.

**Mitigating context:** The 1814 build report (`pipeline/1814-BUILD-REPORT.md`) acknowledges this directly: *"CC flagged that the enum and FileStorageService changes were already present from commit 9c4eaeb (ADO #1813 work). This commit adds the SpecGenerationService switch improvements on top."* The #1814 work landed cleanly on top and reached the correct final state. No functional harm was done — but the WI boundaries were violated.

---

### Consistency Audit

**Cross-references verified:**

- `DiscoveryService.cs` switch uses `FileType.Html`, `FileType.Pdf`, `FileType.Other`, `FileType.Image` — all exist in the enum ✅
- `FileType.Text` added to enum in this commit — not referenced in `DiscoveryService.cs` (correct; #1814 adds that) ✅
- `FileStorageService.cs` now routes text/plain → `FileType.Text` — consistent with enum addition ✅
- No cross-file value mismatch found ✅

---

### Critical Issues — 0

#### Critical #1: UploadedFile Null Guard — ✅ PASS

```csharp
var files = submission.SubmissionFiles
    .Select(sf => sf.UploadedFile)
    .Where(f => f != null)
    .ToList();
```

`.Where(f => f != null)` filters out null junction records before any property access. Inside the `foreach`, `file!.OriginalFileName` uses the null-forgiving operator correctly — the LINQ chain returns `UploadedFile?` regardless, but no null can reach this point. `file.FileType` and `file.ProcessedText` are fully guarded.

#### Critical #2: Truncation Guard — ✅ PASS

```csharp
if (!string.IsNullOrWhiteSpace(file.ProcessedText))
{
    var content = file.ProcessedText.Length > 2000
        ? file.ProcessedText[..2000] + "\n... [truncated]"
        : file.ProcessedText;
    ...
}
```

(a) `string.IsNullOrWhiteSpace(file.ProcessedText)` is checked BEFORE `.Length` — no NullReferenceException possible.
(b) `[..2000]` is only executed when `Length > 2000` — the ternary condition guarantees the slice is always in bounds. No `ArgumentOutOfRangeException` risk.

#### Critical #3: No S3 Downloads — ✅ PASS

No `DownloadAsync`, `GetObjectAsync`, `_s3`, `S3Client`, or any storage download calls in the new block. `GenerateQuestionsAsync` reads only `ProcessedText` from the EF-loaded entity (already in DB). Zero S3 calls.

---

### Important Issues — 1

#### I1: Out-of-Scope FileType Infra Bundled into #1813
- **Files:** `Models/Enums/FileType.cs`, `Services/FileStorageService.cs`
- **Category:** Scope violation
- **Issue:** `FileType.Text` enum value and `FileStorageService.cs` routing (MIME type → `FileType.Text`, UTF-8 decode path) were bundled into commit `9c4eaeb` for #1813. The review brief for #1813 explicitly states these changes belong in #1814.
- **Impact:** Work item boundaries are blurred. If #1813 is reverted for any reason, it silently removes infra that #1814 depends on. The #1814 build report acknowledges the collision and built on top successfully — so there's no functional regression — but the coupling is undocumented at the WI level.
- **Fix:** One of two options:
  - **Option A (preferred):** Add a note to ADO #1813 acknowledging the `FileType.Text` infra was pulled forward, and update #1814 to reflect it only adds `SpecGenerationService.cs` routing (which is what `22d1dd2` actually did). WIs remain valid, just update their descriptions.
  - **Option B:** No code change needed — #1814 landed cleanly. Just document the WI overlap so future reviewers understand the history.

---

### Important — Checks Passed

#### Important #4: FileType.Other Stand-In — ✅ PASS

```csharp
case FileType.Html:
case FileType.Pdf:
case FileType.Other: // FileType.Text added in #1814
```

`Other` fall-through includes file contents (not the binary fallback). The `// FileType.Text added in #1814` comment is present. ✅

#### Important #5: Image Case Correctness — ✅ PASS

```csharp
case FileType.Image:
    userPromptSb.AppendLine("*[Image file — visual content not included in question generation]*");
    break;
```

No access to `file.ProcessedText` (which is null for images). Safe. ✅

#### Important #6: Default Case — ✅ PASS

`default:` appends `*[Binary or unsupported file type]*` — no ProcessedText access. Safe fallback. ✅

---

### Nitpicks — 0

None.

---

### Positive Observations

- The null guard pattern (`.Where(f => f != null)` + `!` in foreach) is clean and idiomatic C#.
- The truncation guard using `string.IsNullOrWhiteSpace` wrapping the `.Length` check is exactly the right order of operations — no NRE risk.
- The `// FileType.Text added in #1814` comment on `case FileType.Other:` is excellent — marks the known tech debt clearly for the next engineer.
- No S3 calls — the constraint to use only `ProcessedText` was respected. Prompt assembly stays purely in-memory/DB.
- The Image fallback correctly produces a human-readable skip message rather than empty output or an error.

---

### What To Fix

1. **I1 (Scope):** No code change required. Update ADO #1813 with a note: *"FileType.Text enum value and FileStorageService routing were bundled into this commit; #1814 consumed these and added SpecGenerationService switch improvements."* Update #1814 description to reflect its actual scope (SpecGenerationService only, plus the Text routing that landed in #1813).

This is an administrative fix, not a code fix. The functional code is correct and safe.
