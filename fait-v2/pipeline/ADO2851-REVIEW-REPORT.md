# Review Report — ADO#2851

**WI:** FAIT v2: Memory management UI  
**Commit:** `c586d08`  
**Reviewer:** Hawkeye (Clint Barton)  
**Cycle:** 1  
**Date:** 2026-05-07  

---

### Verdict: NEEDS-CHANGES

---

### Spec Compliance Check

Not applicable — no developer brief path was provided for this WI. Review proceeded on code quality and security grounds.

---

### Build Status

**Build: ✅ SUCCEEDED** (0 code errors)  
Note: `MSB3492` cache artifact fires every run in this WSL environment — known fluke, not a code issue.

---

### Consistency Audit

**Files Reviewed:**
- `Components/Memory/TopicList.razor`
- `Services/IMemoryFileService.cs`

**Interface alignment:**
- `TopicList` calls `GetTopicsAsync`, `UpsertTopicAsync`, `DeleteTopicAsync` — all three exist in `IMemoryFileService`. ✅
- S3 key pattern confirmed in interface doc comment: `workspaces/{userId}/memory/topics/{topicSlug}.md` — slug is used **directly** in the S3 key path. This is what makes C1 a real security issue.

---

### Critical Issues — 1

#### C1: Slug not sanitized — path traversal risk in S3 key
- **File:** `Components/Memory/TopicList.razor` — `ConfirmCreate()` method
- **Category:** Security / Input Validation
- **Issue:** The slug is processed as `_newSlug.Trim().ToLower().Replace(" ", "-")` before being passed to `UpsertTopicAsync`. This strips spaces but does **not** strip `..`, `/`, `\`, or other path-traversal characters. The `IMemoryFileService` doc comment confirms the slug flows directly into the S3 key: `workspaces/{userId}/memory/topics/{topicSlug}.md`. A slug of `../../evil` would resolve to `workspaces/{userId}/evil.md` — outside the user's topic prefix.
- **Confirmed:** ✅ No additional sanitization exists in `ConfirmCreate`. `DeleteTopic` has the same gap (it passes `slug` directly, but slug comes from the loaded topic list, so lower risk there).
- **Evidence:**
  ```csharp
  private async Task ConfirmCreate()
  {
      if (string.IsNullOrWhiteSpace(_newSlug)) return;
      var slug = _newSlug.Trim().ToLower().Replace(" ", "-");
      // No further sanitization — slug goes straight into S3 key
      await MemoryService.UpsertTopicAsync(UserId, slug, $"# {slug}\n\n");
  ```
- **Impact:** Authenticated user can write files outside their topic prefix in S3. Depending on IAM policy, this could overwrite other users' files or system files in the same bucket.
- **Fix:**
  ```csharp
  private async Task ConfirmCreate()
  {
      if (string.IsNullOrWhiteSpace(_newSlug)) return;
      var slug = Regex.Replace(_newSlug.Trim().ToLower().Replace(" ", "-"), @"[^a-z0-9\-_]", "");
      if (string.IsNullOrEmpty(slug)) return;
      await MemoryService.UpsertTopicAsync(UserId, slug, $"# {slug}\n\n");
  ```
  Also add `@using System.Text.RegularExpressions` at top if not already present.

---

### Important Issues — 1

#### I1: Re-render storm — `OnParametersSetAsync` calls `LoadTopics` on every render
- **File:** `Components/Memory/TopicList.razor` — `OnParametersSetAsync()`
- **Category:** Correctness / Performance
- **Issue:** Blazor calls `OnParametersSetAsync` on every render cycle when parameters are set, including parent `StateHasChanged` calls. The current implementation unconditionally calls `LoadTopics()` (an S3 network call) whenever `UserId` is non-empty — with no guard checking whether `UserId` actually changed. In a component that's part of a larger page with frequent re-renders (e.g. typing in a `TopicEditor`), this fires an S3 `GetTopicsAsync` on every keystroke.
- **Confirmed:** ✅ No `_lastLoadedUserId` guard or equivalent change detection exists.
- **Evidence:**
  ```csharp
  protected override async Task OnParametersSetAsync()
  {
      if (!string.IsNullOrEmpty(UserId))
          await LoadTopics();   // fires on every parent re-render
  }
  ```
- **Impact:** Excessive S3 reads; visible `_loading = true` flicker; potential race conditions if rapid re-renders overlap async responses.
- **Fix:**
  ```csharp
  private string _lastLoadedUserId = "";

  protected override async Task OnParametersSetAsync()
  {
      if (!string.IsNullOrEmpty(UserId) && UserId != _lastLoadedUserId)
      {
          _lastLoadedUserId = UserId;
          await LoadTopics();
      }
  }
  ```

---

### Nitpicks — 0

None.

---

### Positive Observations

- Clean component structure — `StartCreate` / `CancelCreate` / `ConfirmCreate` flow is clear and readable.
- Interface doc comments are thorough and include S3 key patterns — this is what allowed C1 to be confirmed quickly.
- Keyboard handling (`HandleCreateKey`) is a nice UX touch.

---

### What Tony Needs to Fix

1. **C1** — Add `Regex.Replace` slug sanitization in `ConfirmCreate` (see fix above). Also consider whether the `MemoryFileService` implementation should validate slugs defensively as a second layer.
2. **I1** — Add `_lastLoadedUserId` guard in `OnParametersSetAsync` (see fix above).

Both fixes are small, isolated, and straightforward. No architectural changes required.

---

## Review Report — ADO#2851 — Cycle 2 (Verification)

**Commit:** `8923ed7`  
**Reviewer:** Hawkeye (Clint Barton)  
**Cycle:** 2 (final)  
**Date:** 2026-05-07  

---

### Verdict: ✅ PASS

---

### CC Invocation
```bash
cat review-c2-2851-brief.md | claude --model sonnet --print --dangerously-skip-permissions
```
CC confirmed both fixes present and correct. No regressions observed.

---

### C1 Verification — Slug path-traversal sanitization

- **Line 90–92:** `Regex.Replace(_newSlug.Trim().ToLower().Replace(" ", "-"), @"[^a-z0-9\-_]", "")` — allowlist regex strips all non-safe characters including `..`, `/`, `\`.
- **Line 93:** Empty-slug early return present: `if (string.IsNullOrEmpty(slug)) return;`
- **Verdict:** ✅ C1 fix correct and complete.

---

### I1 Verification — OnParametersSetAsync re-render guard

- **Line 66:** `_lastLoadedUserId` field declared at class level, initialized to `""`.
- **Line 70:** Guard condition `UserId != _lastLoadedUserId` prevents redundant S3 calls.
- **Line 72:** `_lastLoadedUserId = UserId` set *before* `await LoadTopics()` — correct ordering prevents duplicate loads on rapid re-renders.
- **Verdict:** ✅ I1 fix correct and complete.

---

### Build

**✅ 0 errors, 0 warnings** (`dotnet build --no-incremental`)

---

### Summary

Both required fixes from Cycle 1 have been correctly implemented. No new issues introduced. Build clean. ADO#2851 is ready to proceed.
