# Build Report — ADO #1814 — Spec Generation Attachment Type Routing Fix

## What was built
Added `FileType.Text` enum value to properly route text-based files (`.md`, `.txt`, `.json`)
through a dedicated path in `BuildPromptAsync`, replacing the generic `Other` catch-all.
Added clear section headers to all FileType cases in the prompt builder switch.

## Files changed

- `Models/Enums/FileType.cs` — Added `Text` value between `Pdf` and `Other`
- `Services/FileStorageService.cs` — `DetectFileType` now maps `text/plain`, `text/markdown`,
  `text/x-markdown`, `application/json` → `FileType.Text`; `UploadAsync` fires UTF-8 decode
  for `FileType.Text || FileType.Other`
- `Services/SpecGenerationService.cs` — `BuildPromptAsync` switch split into 5 explicit cases
  (Html, Image, Text, Pdf, Other+default); each case emits `**File Type: X**` header; Text case
  also emits `**File Contents: {filename}**` before content; Pdf now has its own case

## Commit
`22d1dd2` — feat(nexus#1814): add FileType.Text enum + routing for text/markdown/json files

## Note from CC output
CC flagged that the enum and FileStorageService changes were already present from commit `9c4eaeb`
(ADO #1813 work). This commit adds the SpecGenerationService switch improvements on top.

## Parallelization used
No — single-file context dependency required sequential execution.

## CC sessions run
1 — CC Sonnet, single pass.

## Acceptance criteria verification
- [x] `FileType` enum has 5 values: Html, Image, Pdf, Text, Other — ✅
- [x] `DetectFileType` maps text/plain, text/markdown, text/x-markdown, application/json → `FileType.Text` — ✅
- [x] `UploadAsync` sets processedText for `FileType.Text` (UTF-8 path) — ✅
- [x] `BuildPromptAsync` switch has 5 explicit cases — ✅
- [x] Each case emits `**File Type: X**` header — ✅
- [x] Text case emits `**File Contents: {filename}**` before content — ✅
- [x] Pdf case is its own explicit case (no longer grouped with Other) — ✅
- [x] `dotnet build` — 0 errors, 0 warnings — ✅

## Known edge cases / things Clint should scrutinize
- **Pdf with null ProcessedText** — still falls to "*PDF file — no text content available*" message.
  No S3 re-extraction attempted (pre-dates extraction or failed at upload). This is an acceptable
  limitation; the error message is now more descriptive.
- **FileType.Other** — remains a catch-all for truly unknown binary types. If `ProcessedText` is
  non-null it will be emitted; in practice this should be rare.
- The enum change is additive and non-breaking — no DB migration needed (FileType is stored as
  string/int and `Other` still exists for legacy records).

## How to test locally
1. Upload a `.md` or `.json` file in the NewSpecWizard
2. Trigger spec generation
3. Check the generated prompt (via logs or debug) for `**File Type: Text**` and
   `**File Contents: filename.md**` headers
4. Verify a `.pdf` upload still gets `**File Type: PDF**` header with extracted text

---

## Cycle 2 — 2026-04-13

### What was built
Two surgical switch-case fixes resolving the FileType.Text routing gap introduced in Cycle 1.

### Files changed
- `Services/Discovery/DiscoveryService.cs` — Added `case FileType.Text:` as fall-through before `case FileType.Other:` in `GenerateQuestionsAsync`. Removed stale `// FileType.Text added in #1814` comment. Both Text and Other now share the content-inclusion block.
- `Services/SpecGenerationService.cs` — `case FileType.Other: default:` in `BuildPromptAsync` no longer emits ProcessedText. Now emits "Unknown/Unsupported" label + binary-skip message only. Text files route to their own `FileType.Text` case above.

### CC sessions run
1 — single CC Sonnet run, both fixes in one pass.

### Build result
`dotnet build` — **0 errors, 0 warnings**

### Commit
`2708502`

### Acceptance criteria
- [x] `case FileType.Text:` present in DiscoveryService switch — verified by CC + build
- [x] `Other`/`default` in SpecGenerationService emits binary-only skip message — verified by CC + build
- [x] `dotnet build` 0 errors
