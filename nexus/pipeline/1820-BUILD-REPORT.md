# Build Report — ADO #1820 — Discovery prompt truncation limits

**Date:** 2026-04-14
**Builder:** Tony Stark (software-engineer)
**Branch:** working tree (uncommitted, based on 7de0146)
**Risk:** Low — constants only, no logic change

---

## What was built

Expanded Discovery prompt truncation limits to allow richer context in AI question generation:
- Narrative KB query: 500 → 1500 chars
- Per-file text: 2000 → 8000 chars
- Added 20K combined file cap with graceful truncation
- MaxTokens response budget: 2048 → 4096

---

## Files changed

- `Services/Discovery/DiscoveryService.cs`
  - Line 249: Narrative truncation limit `500` → `1500`
  - Line 294–295: Added `totalFileChars = 0` / `MaxTotalFileChars = 20_000` declarations before `imageCount`
  - Lines 309–321: Replaced 2000-char per-file truncation with 8000-char per-file + 20K combined cap logic. Cap check fires at loop top; if reached, appends `*[File cap reached — remaining files omitted]*` and breaks the switch case (intentional — foreach continues so header is still written for skipped files)

- `Services/Discovery/DiscoveryInferenceConfig.cs`
  - Line 10: `MaxTokens` default `2048` → `4096`

---

## Parallelization used

No — single CC session, sequential. Two files, trivial edits, no benefit from parallelizing.

---

## CC sessions run

1 session — CC Sonnet, pipe mode. Clean first pass.

---

## Acceptance criteria verification

- [x] `narrativeTruncated` uses `> 1500` / `[..1500]` — confirmed line 249
- [x] Per-file truncation uses `> 8000` / `[..8000]` — confirmed line 316–317
- [x] `totalFileChars` / `MaxTotalFileChars = 20_000` declared before `imageCount` — confirmed lines 294–295
- [x] Cap check + graceful truncation inside text file switch block — confirmed lines 310–321
- [x] `MaxTokens = 4096` in DiscoveryInferenceConfig — confirmed line 10
- [x] `dotnet build` — **0 errors** (1 pre-existing warning in FileStorageService.cs:148, unrelated)

---

## Known edge cases / things Clint should scrutinize

- The `break` inside the switch case for `totalFileChars >= MaxTotalFileChars` breaks the **switch**, not the foreach. The foreach continues to subsequent files where the cap check fires again on each iteration. This means a file header (`### filename.pdf`) is written to the prompt for a capped file before the switch fires — intentional, so Fred can see the file existed. If Clint prefers the header to be suppressed when capped, that's a 2-line change.
- The `remaining` variable is computed as `MaxTotalFileChars - totalFileChars`. If a file is partially included (content sliced to `remaining`), `totalFileChars` is incremented by `content.Length` after the slice — so the next file's cap check will fire at exactly 20K. Math is correct.
- No overflow risk: `MaxTotalFileChars = 20_000`, content is a string slice, all ints.

---

## How to test locally

```bash
cd ~/projects/fip/nexus/src/FortressNexus.Web
dotnet build
# Submit a Discovery feature request with multiple large attached text files
# Verify prompt context is richer, AI questions are more detailed
# Verify no prompt exceeds ~20K of file content
```

---

## Build result

```
Build succeeded.
0 Error(s)
1 Warning(s) — pre-existing, unrelated (FileStorageService.cs:148)
```
