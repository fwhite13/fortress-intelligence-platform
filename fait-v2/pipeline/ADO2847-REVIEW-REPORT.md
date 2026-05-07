# Review Report — ADO#2847

**WI:** FAIT v2: Memory file service - S3 read/write, memory topic CRUD, pgvector index sync
**Commit:** `13243e0`
**Reviewer:** Hawkeye (Clint Barton)
**Review cycle:** 1 of 2
**Date:** 2026-05-07

---

### Verdict: NEEDS-CHANGES

---

### CC Review Summary

CC ran adversarial analysis against `IMemoryFileService.cs`, `MemoryFileService.cs`, and the EF model for collision context. 13 checks executed.

**All critical checklist items passed.** Structural implementation is sound — correct ZIP construction, correct 404 handling on reads, no hardcoded credentials, no cross-user data leakage in list ops, correct DI registration.

**6 Important issues found, 3 Nitpicks.** The blockers for PASS are:
1. Inner `GetObjectAsync` calls in `GetTopicsAsync` (and `ExportZipAsync`) have no error handling — a single race or transient error nukes the entire list operation.
2. `userId` and `topicSlug` are interpolated directly into S3 keys with no validation — security concern, especially for `topicSlug` which is user-supplied content.
3. `DeleteTopicAsync` has no `RemoveFromVectorIndexAsync` stub — when Sprint 2 wires pgvector, deleted topics will orphan in the index silently.

Items #5 (N+1 in GetTopics) and #6 (ExportZip double-load) are pre-acknowledged by Tony as Sprint 2 items — not blocking.

---

### Spec Compliance Check

**§ Codebase Map:**
- `Services/IMemoryFileService.cs` — ✅ Created
- `Services/MemoryFileService.cs` — ✅ Created
- `Program.cs` — ✅ Modified (DI registration)

**§ Out of Scope:**
- ✅ No out-of-scope changes detected

**§ Acceptance Criteria:**
- [x] `IMemoryFileService` + `MemoryFileService` with all 9 methods — ✅ Verified (ReadFileAsync, WriteFileAsync, DeleteFileAsync, ListFilesAsync, GetTopicsAsync, GetTopicAsync, UpsertTopicAsync, DeleteTopicAsync, ExportZipAsync)
- [x] S3 prefix `workspaces/{userId}/memory/` used consistently — ✅ Verified via `FileKey()` and `MemoryPrefix()` helpers
- [x] Topic files at `workspaces/{userId}/memory/topics/{topicSlug}.md` — ✅ Verified via `TopicKey()`
- [x] No hardcoded AWS credentials, region, or bucket name — ✅ `AWS:WorkspaceBucket` config key used; `IAmazonS3` injected; no `new AmazonS3Client()`
- [x] `ExportZipAsync` returns valid ZIP bytes via `System.IO.Compression.ZipArchive` — ✅ `leaveOpen: true`, archive disposed before `ToArray()`, structure correct
- [x] pgvector sync stubbed with TODO — ✅ `SyncToVectorIndexAsync` no-op with Sprint 2 TODO
- [x] Registered in Program.cs — ✅ `AddScoped<IMemoryFileService, MemoryFileService>()` at line 72
- [x] Build clean — ✅ Per build report (0 errors, 0 warnings)

**Spec compliance verdict:** ✅ COMPLIANT (all criteria met; blockers are implementation correctness issues)

---

### Consistency Audit

**Files Cross-Referenced:**
- `IMemoryFileService.cs` ↔ `MemoryFileService.cs` — ✅ All 9 interface methods implemented; signatures match exactly
- `MemoryFileService.cs` S3 keys ↔ prefix helpers — ✅ `FileKey`, `TopicKey`, `MemoryPrefix`, `TopicsPrefix` all consistent with spec prefixes
- `Data/Models/MemoryTopic.cs` ↔ `Services/MemoryTopicEntry` — ✅ Different namespaces (`Data.Models` vs `Services`), different names — no compiler ambiguity; rename was correct and sufficient
- `Program.cs:67` (`AddAWSService<IAmazonS3>()`) ↔ `MemoryFileService` constructor (`IAmazonS3 s3`) — ✅ DI contract matches

**Undocumented Dependencies:**
- None found

---

### Important Issues (6)

#### I1: Inner `GetObjectAsync` in `GetTopicsAsync` — unhandled exception
- **File:** `MemoryFileService.cs` ~lines 152–158
- **Category:** Correctness / Reliability
- **Issue:** For each topic key returned by `ListObjectsV2`, a separate `GetObjectAsync` is issued with no try/catch. A race condition (object deleted between list and get) or any transient S3 error throws an unhandled exception, killing the entire `GetTopicsAsync` call and returning a 500. Same pattern in `ExportZipAsync` lines ~253–261.
- **Impact:** A single missing or transient file makes all topic listing unavailable.
- **Fix:**
  ```csharp
  try
  {
      var getResponse = await _s3.GetObjectAsync(..., ct);
      using var reader = new StreamReader(getResponse.ResponseStream);
      var content = await reader.ReadToEndAsync(ct);
      topics.Add(new MemoryTopicEntry(slug, content, obj.LastModified));
  }
  catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
  {
      _logger.LogWarning("Topic key {Key} listed but not found — skipping", obj.Key);
  }
  ```
  Apply same pattern to per-object gets in `ExportZipAsync`.

#### I2: S3 delete `AmazonS3Exception` 404 catch is dead code
- **File:** `MemoryFileService.cs` ~lines 91–94 and 224–227
- **Category:** Correctness (misleading/dead code)
- **Issue:** AWS S3 `DeleteObject` is idempotent — it always returns HTTP 204 whether or not the key existed. The SDK never throws a 404 `AmazonS3Exception` for deleting a non-existent key. Both `DeleteFileAsync` and `DeleteTopicAsync` have try/catch blocks for this that can never fire on production AWS.
- **Impact:** No behavioral problem today; misleading intent. If LocalStack or MinIO dev environment behaves differently, the discrepancy matters.
- **Fix:** Remove the try/catch from both delete methods and add a comment:
  ```csharp
  // S3 DeleteObject is idempotent — returns 204 whether or not the key exists
  await _s3.DeleteObjectAsync(new DeleteObjectRequest { BucketName = _bucket, Key = key }, ct);
  _logger.LogInformation("Deleted memory file {Key}", key);
  ```

#### I3: `userId` not validated before S3 key construction
- **File:** `MemoryFileService.cs` ~line 32 (`FileKey` helper) + all public methods
- **Category:** Security
- **Issue:** `userId` is interpolated directly into S3 keys with no validation. A crafted value like `user-a/../user-b` generates key `workspaces/user-b/memory/...` — a genuine cross-user access path. S3 does not normalize path separators. Risk is low if userId is always a UUID from a trusted auth layer, but there is zero defensive protection.
- **Fix:** Add a guard in each public method or centrally in the helpers:
  ```csharp
  private static void ValidateId(string id, string paramName)
  {
      if (!System.Text.RegularExpressions.Regex.IsMatch(id, @"^[a-zA-Z0-9\-_]{1,64}$"))
          throw new ArgumentException($"Invalid {paramName}: contains disallowed characters.", paramName);
  }
  ```

#### I4: `topicSlug` not validated before S3 key construction
- **File:** `MemoryFileService.cs` ~line 35 (`TopicKey` helper)
- **Category:** Security
- **Issue:** `topicSlug` is user-supplied content used directly in S3 key construction. A slug containing `/` (e.g., `foo/bar`) creates a key at `workspaces/{userId}/memory/topics/foo/bar.md`, placing it in an unexpected subdirectory and potentially confusing `GetTopicsAsync` prefix matching. A slug like `../../admin` could escape the topics prefix. Higher risk than userId since slugs come from user input, not auth tokens.
- **Fix:** Apply same validation pattern — `^[a-zA-Z0-9\-_]{1,200}$` enforced in `TopicKey` or the public topic methods.

#### I5: `GetTopicsAsync` N+1 S3 requests — serial per-topic fetch
- **File:** `MemoryFileService.cs` ~lines 129–166
- **Category:** Performance
- **Issue:** For each topic returned by `ListObjectsV2`, a separate serial `GetObjectAsync` call is issued. 100 topics = 101 sequential S3 round-trips in a single request. No parallelism.
- **Impact:** Pre-acknowledged by Tony as Sprint 2 item (build report note). Flagging for tracking.
- **Fix (Sprint 2):** Either parallelize with `Task.WhenAll`, or change `GetTopicsAsync` to return slugs-only (no content load) and rely on callers using `GetTopicAsync` for individual content fetches.

#### I6: `DeleteTopicAsync` missing `RemoveFromVectorIndexAsync` stub
- **File:** `MemoryFileService.cs` ~line 212 (`DeleteTopicAsync`)
- **Category:** Correctness (forward-looking)
- **Issue:** `UpsertTopicAsync` calls `SyncToVectorIndexAsync` (the add/update stub). `DeleteTopicAsync` has no corresponding stub for removal. When Sprint 2 wires real pgvector sync, deleted topics will remain as orphaned vectors in the index — they'll surface in semantic search results for content that no longer exists.
- **Fix:** Add a stub alongside `SyncToVectorIndexAsync`:
  ```csharp
  // Sync removal from pgvector index (stub — Sprint 2)
  await RemoveFromVectorIndexAsync(userId, topicSlug);
  ```
  And add the companion stub method:
  ```csharp
  private static Task RemoveFromVectorIndexAsync(string userId, string topicSlug)
  {
      // No-op stub — pgvector removal is Sprint 2
      return Task.CompletedTask;
  }
  ```

---

### Nitpicks (3)

- **N1: `UpsertTopicAsync` returns approximate timestamp** (`MemoryFileService.cs:203,209`) — Returns `DateTimeOffset.UtcNow` rather than S3's actual `LastModified`. Low risk; callers should not rely on it for sync logic. Not blocking.
- **N2: `SyncToVectorIndexAsync` stub missing `CancellationToken` parameter** (`MemoryFileService.cs:292`) — When Sprint 2 implements real async I/O, the signature change will touch all call sites. Add `CancellationToken ct = default` now.
- **N3: `MemoryTopicEntry` rename confirmed correct** — EF entity is `Data.Models.MemoryTopic`; DTO is `Services.MemoryTopicEntry`. Different namespaces + different names = no ambiguity. Rename was the right call (vs. fully-qualifying the EF entity). No action needed.

---

### Positive Observations

- Clean S3 key design with private helpers (`FileKey`, `TopicKey`, `MemoryPrefix`, `TopicsPrefix`) — single definition, not copy-pasted throughout.
- Correct `leaveOpen: true` on `ZipArchive` constructor — many implementations get this wrong and return an invalid ZIP.
- Full S3 pagination via continuation tokens in all list operations.
- `CancellationToken` threaded through all SDK calls correctly.
- `AWS:WorkspaceBucket` guard (`?? throw`) catches misconfiguration at startup — better than silently failing on first S3 call.
- pgvector stub documented with ticket reference — won't get lost.
- XML doc comments on all interface methods. ✅

---

### What Tony Needs to Fix

**Mandatory before cycle 2 PASS:**

1. **Wrap inner `GetObjectAsync` calls** in `GetTopicsAsync` and `ExportZipAsync` with a try/catch for `AmazonS3Exception` 404 — log warning, skip entry, continue.

2. **Remove the dead 404 catch blocks** from `DeleteFileAsync` and `DeleteTopicAsync`. Replace with a comment noting S3 delete is idempotent.

3. **Validate `userId`** — add a guard that rejects values not matching `^[a-zA-Z0-9\-_]{1,64}$` (or appropriate UUID pattern).

4. **Validate `topicSlug`** — add a guard that rejects values not matching `^[a-zA-Z0-9\-_]{1,200}$` before use in `TopicKey`.

5. **Add `RemoveFromVectorIndexAsync` stub** to `DeleteTopicAsync` with a Sprint 2 TODO comment, parallel to the existing `SyncToVectorIndexAsync` stub.

Items I5 (N+1), I6 (ExportZip double-load), N1 (timestamp), and N2 (ct on stub) can follow in Sprint 2 or be bundled with cycle 2 fixes at Tony's discretion.

---

_Hawkeye — Review cycle 1 of 2. NEEDS-CHANGES._

---

## Review Report — ADO#2847 Cycle 2

**Commit:** `aa14724`
**Reviewer:** Hawkeye (Clint Barton)
**Review cycle:** 2 of 2 (final)
**Date:** 2026-05-07

---

### Verdict: PASS

---

### CC Review Summary

CC ran adversarial cycle 2 verification against `MemoryFileService.cs` and `IMemoryFileService.cs`. All 5 mandatory fix checks executed.

**All 5 fixes verified correct.** No regressions found. Build clean (0 errors, 0 warnings per Tony's cycle 2 build report).

CC command:
```bash
cd /home/fredw/projects/fip/fait-v2/src/FortressAI.V2.Web && \
  CLAUDE_CODE_ENTRYPOINT=ado-pipeline CLAUDE_CODE_DISABLE_AUTO_MEMORY=1 \
  cat /tmp/review-c2-brief.md | claude --model sonnet --print --dangerously-skip-permissions
```

---

### Fix Verification

| Fix | Issue | Result |
|-----|-------|--------|
| I1 | Inner `GetObjectAsync` in `GetTopicsAsync` wrapped in per-file try/catch (NoSuchKey → warn+skip, Exception → error+skip, no rethrow) | ✅ VERIFIED |
| I1 | Same wrapping applied to inner `GetObjectAsync` in `ExportZipAsync` | ✅ VERIFIED |
| I2 | Dead 404 catch removed from `DeleteFileAsync`; unconditional `DeleteObjectAsync` with idempotency comment | ✅ VERIFIED |
| I2 | Dead 404 catch removed from `DeleteTopicAsync`; same unconditional pattern | ✅ VERIFIED |
| I3 | `ValidateId(userId, ...)` called at top of all 9 public methods | ✅ VERIFIED |
| I4 | `ValidateId(topicSlug, ...)` called at top of all 3 topic methods (`GetTopicAsync`, `UpsertTopicAsync`, `DeleteTopicAsync`) | ✅ VERIFIED |
| I6 | `RemoveFromVectorIndexAsync(userId, topicSlug, ct)` called in `DeleteTopicAsync`; no-op stub with TODO alongside `SyncToVectorIndexAsync` | ✅ VERIFIED |

**`ValidateId` logic verified manually:** rejects empty, whitespace, `/`-containing, and `..`-containing values; accepts valid identifiers like `valid-user-123`. Guard is correct.

---

### Regression Check

- Interface contract: all 9 signatures match implementation exactly — ✅ no breakage
- Existing methods (`ReadFileAsync`, `WriteFileAsync`, `ListFilesAsync`): structurally unchanged — ✅ no damage
- New try/catch blocks scope correctly — swallow per-object errors only, outer `ListObjectsV2Async` failures still propagate — ✅ correct
- No new holes introduced

---

### Observations (non-blocking, backlog only)

- **Redundant TODO comment** (`DeleteTopicAsync` ~line 247): comment says "remove from pgvector index when wired" immediately above the call that already does it (as stub). Harmless contradiction — clean up opportunistically.
- **`fileName` parameter not validated** in `ReadFileAsync`, `WriteFileAsync`, `DeleteFileAsync`: `userId` and `topicSlug` are now guarded; `fileName` is not. S3 keys are opaque (no path normalization), so practical risk is low, but worth noting for a future pass.

Neither item blocks PASS. Both are pre-existing or cleanup-level.

---

### Sprint 2 Backlog (confirmed pre-acknowledged)
- I5: N+1 serial `GetObjectAsync` in `GetTopicsAsync` — parallelize with `Task.WhenAll`
- N1: `UpsertTopicAsync` returns approximate timestamp
- N2: `SyncToVectorIndexAsync` missing `CancellationToken` parameter
- `fileName` validation (above)

---

_Hawkeye — Review cycle 2 of 2. PASS. Ready for CodeSec scan and deployment._
