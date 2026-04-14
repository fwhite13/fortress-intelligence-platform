# Build Report — ADO #1819
## Discovery image vision calls in GenerateQuestionsAsync

**Date:** 2026-04-13  
**Builder:** Tony Stark (software-engineer)  
**Commit:** `34a0ba4`  
**Branch:** `origin/main`

---

### What was built
`DiscoveryService.GenerateQuestionsAsync` now calls `_bedrock.InvokeWithImageAsync` for `FileType.Image` attachments (max 3, 2 attempts each, graceful fallback on timeout or failure). `IFileStorageService` and `SpecGenInferenceConfig` injected into `DiscoveryService`.

---

### Files changed
- `Services/Discovery/DiscoveryService.cs`
  - Added `using FortressNexus.Web.Services;`
  - Added `_fileStorage` (`IFileStorageService`) and `_specGenConfig` (`SpecGenInferenceConfig`) private fields
  - Updated constructor to accept `IOptions<SpecGenInferenceConfig> specGenConfig` and `IFileStorageService fileStorage`
  - Added `int imageCount = 0;` before the `foreach (var file in files)` loop
  - Replaced `FileType.Image` stub with full vision call: download from S3, invoke `InvokeWithImageAsync`, inject description or fall back gracefully

---

### Parallelization used
No — single sequential CC session (single file, ordered changes required).

### CC sessions run
1 CC Sonnet session — brief piped via stdin with exact field names pre-verified.

---

### Acceptance criteria verification
- [x] `_fileStorage` and `_specGenConfig` injected as private fields
- [x] Constructor updated with `IOptions<SpecGenInferenceConfig>` and `IFileStorageService` params
- [x] `int imageCount = 0;` declared before foreach loop
- [x] `FileType.Image` case: 3-image cap, 2-attempt retry, per-attempt `CancellationTokenSource` with `TimeoutSeconds`, graceful fallback strings on timeout and general exception
- [x] `using FortressNexus.Web.Services;` added
- [x] `dotnet build` — **0 errors** (1 pre-existing warning in `FileStorageService.cs:148`, not touched by this change)

---

### Known edge cases / things Clint should scrutinize
1. **`submission.Title` in the vision prompt** — the variable `submission` is in scope at the `FileType.Image` case (it's the parent submission object loaded earlier in `GenerateQuestionsAsync`). Confirm it's in scope at the switch block — it should be, but worth a read-through.
2. **`DownloadAsync` returns `Stream`** — the `using var ms` pattern correctly drains it to a `MemoryStream`. If the stream is already disposed upstream, this will throw and the outer `catch (Exception ex)` will handle it gracefully.
3. **Token budget** — each image uses up to 512 output tokens. With 3 images max, that's ~1536 tokens of image description added to the discovery prompt. Should be fine for the question-gen context window.
4. **Pre-existing warning** — `FileStorageService.cs(148)` CS8601 nullable warning is pre-existing, not introduced here.

---

### How to test locally
1. Create a Discovery submission with 1–3 image attachments (PNG/JPEG)
2. Trigger `GenerateQuestionsAsync` for that submission
3. Verify logs show `[DISCOVERY_GEN]` vision call activity
4. Verify generated questions reference visual content from the images
5. Test fallback: submit with an image that can't be downloaded → expect `*[Image: ... — vision failed]*` in the prompt and graceful completion

---

## Cycle 2 — `using var` Stream Disposal Fix

**Commit:** `7de0146`
**Date:** 2026-04-14

### What was fixed
Added `using` keyword to `imageStream` declaration in the `FileType.Image` vision block of `GenerateQuestionsAsync`. This ensures the S3 HTTP connection is properly disposed after the stream is consumed into the `MemoryStream`.

### File changed
- `Services/Discovery/DiscoveryService.cs` — line 330: `var imageStream` → `using var imageStream`

### Build result
- **0 errors**, 1 pre-existing warning (`FileStorageService.cs:148` CS8601 — not touched)

### CC method
- Claude Code CLI (Sonnet) — surgical single-line edit, confirmed line 330 updated correctly

### Notes
This was flagged in Cycle 1 review. The fix is minimal: one token (`using`) added, no other changes.
