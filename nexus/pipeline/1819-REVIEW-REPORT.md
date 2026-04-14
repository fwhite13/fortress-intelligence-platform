# Review Report — ADO #1819

**Task:** Discovery image vision calls — `DiscoveryService.GenerateQuestionsAsync`
**Commit:** `34a0ba4`
**Reviewer:** Hawkeye (code-reviewer)
**Cycle:** 1
**Date:** 2026-04-13

---

### Verdict: NEEDS-CHANGES

---

### Spec Compliance Check

All changed files are within scope (`DiscoveryService.cs` only). No out-of-scope modifications detected. The `FileType.Image` case now calls vision instead of the old static placeholder — spec fidelity confirmed.

**Spec compliance verdict:** ✅ COMPLIANT

---

### CC Review Summary

CC confirmed all five Critical checks and both Important-5/6 checks. Found one real bug (IMPORTANT) that I had already flagged: `imageStream` never disposed. CC also surfaced one additional WARNING (imageCount incremented before the try block). No Critical issues found.

---

### Consistency Audit

| Cross-Check | Result |
|---|---|
| `UploadedFile.S3Key` / `S3Bucket` vs. `DownloadAsync(string s3Key, string bucketName)` | ✅ property names and arg order correct |
| `IOptions<SpecGenInferenceConfig>` constructor param vs. `Configure<SpecGenInferenceConfig>(...)` in Program.cs | ✅ exact match |
| `IFileStorageService` constructor param vs. `AddScoped<IFileStorageService, FileStorageService>()` in Program.cs | ✅ exact match |
| `file.ContentType` passed as `mimeType` to `InvokeWithImageAsync` | ✅ correct parameter position |
| `imageCount` scope (outside vs. inside foreach) | ✅ declared outside loop at line 294 |

---

### Critical Issues — 0

None.

---

### Important Issues — 1

#### I1: `imageStream` not disposed — S3 HTTP connection leak

- **File:** `Services/Discovery/DiscoveryService.cs` (line 330)
- **Category:** Correctness / Resource management
- **Issue:** `var imageStream = await _fileStorage.DownloadAsync(...)` is declared without `using`. After `CopyToAsync` completes, the underlying S3 HTTP response stream is never disposed. If `CopyToAsync` itself throws, the stream is also left open. Every image processed leaks a connection.
- **Evidence:**
  ```csharp
  var imageStream = await _fileStorage.DownloadAsync(file.S3Key, file.S3Bucket);
  using var ms = new MemoryStream();
  await imageStream.CopyToAsync(ms, ct);
  var imageBytes = ms.ToArray();
  // imageStream never disposed
  ```
- **Impact:** HTTP connection pool exhaustion under load; resource leak on every image-bearing submission.
- **Fix:**
  ```diff
  - var imageStream = await _fileStorage.DownloadAsync(file.S3Key, file.S3Bucket);
  + using var imageStream = await _fileStorage.DownloadAsync(file.S3Key, file.S3Bucket);
  ```

---

### Warnings — 1 (non-blocking)

#### W1: `imageCount` incremented before `try` — failed S3 downloads consume image slots

- **File:** `Services/Discovery/DiscoveryService.cs` (line 326)
- **Severity:** Warning — not a correctness failure given the fallback text
- **Issue:** `imageCount++` runs before the `try` block that wraps `DownloadAsync`. If the first 3 images all fail on download (S3 error, network failure), the cap is exhausted with zero successful vision calls. Subsequent images are skipped silently with `*[Additional image — skipped (limit 3)]*` even though no vision was actually attempted.
- **Note:** Graceful fallback text is still appended for the failed images — prompt integrity is maintained. This is a UX/quality concern, not a crash risk. Tony can address in a follow-up if desired.

---

### Nitpicks — 0

---

### Positive Observations

- `#1812` cancellation pattern implemented correctly: all five guards present in the right positions (linked CTS inside loop, `CancelAfter` on attempt, `attemptCts.Token` to SDK, inner OCE guard, outer OCE guard).
- `imageCount` correctly scoped outside the foreach — cap works as intended.
- `submission` null-guard at top of method guarantees non-null at vision call site — Tony's self-flag was correct and the code is clean.
- `file.ContentType` passed dynamically, not hardcoded — correct.
- `maxTokens: 512` at the right parameter position — correct.
- Fallback strings well-formed for prompt rendering.
- DI registrations match constructor types exactly.

---

### What to Fix

**One change required before merge:**

In `Services/Discovery/DiscoveryService.cs`, line 330:

```diff
- var imageStream = await _fileStorage.DownloadAsync(file.S3Key, file.S3Bucket);
+ using var imageStream = await _fileStorage.DownloadAsync(file.S3Key, file.S3Bucket);
```

That's it. One word. Everything else passes.

---

## Cycle 2 — ADO #1819

**Verdict: PASS**

**Commit:** `7de0146`
**Reviewer:** Hawkeye
**Date:** 2026-04-14

### CC Review Summary
Claude Code confirmed `using var imageStream` is present on line 330. Pre-existing `using var ms` on line 331 is intact. Stream usage (`CopyToAsync`) on line 332 is unchanged. No unintended alterations.

### Diff Verified
```diff
-                            var imageStream = await _fileStorage.DownloadAsync(file.S3Key, file.S3Bucket);
+                            using var imageStream = await _fileStorage.DownloadAsync(file.S3Key, file.S3Bucket);
```

One insertion, one deletion. Exactly as specified. Fix is correct and complete.

### Fix Confirmed
✅ `using var imageStream` — S3 stream will now be properly disposed after use.
