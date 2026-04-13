# Review Report — FIRM ADO #1722
# S3 write for summary in VpCallback

**Verdict: NEEDS-CHANGES**
**Cycle:** 1
**Reviewer:** Hawkeye (Clint Barton)
**Commit:** `0495ffe`
**Date:** 2026-04-13

---

## Spec Compliance Check

**Scope (§ Scope from task):** `S3Service.cs` (+14 lines) and `MeetingsApiController.cs` (+20 lines). No other files.

**git show 0495ffe --stat:**
```
2 files changed, 34 insertions(+)
 .../Controllers/MeetingsApiController.cs | 20 ++++++++++++++++++++
 .../Services/S3Service.cs                | 14 ++++++++++++++
```

✅ Exactly 2 files changed. Scope compliant.

---

## Consistency Audit

**Key convention cross-check:**
- VpCallback write (line 275): `TranscriptS3Key.Replace("transcript.json", "summary.md")`
- DownloadSummary read (line 393): `TranscriptS3Key.Replace("transcript.json", "summary.md")`

✅ Identical. Write and read use the same key derivation — no mismatch.

**Bucket cross-check:**
- `S3Service.Bucket` → `_config["Firm:S3Bucket"]` — used by `GetTranscriptTextAsync`, `GetSummaryTextAsync`, and new `UploadTextAsync`
- `FirmKbService.BucketName` → `_config["Firm:KbS3Bucket"]` — separate KB bucket, writes only

✅ `UploadTextAsync` writes to the same bucket read by `GetTranscriptTextAsync` / `GetSummaryTextAsync`.

---

## CC Review Summary

CC ran full adversarial review of all 3 files + scope check + key convention check.

**Dismissed as false positives:** None — CC findings are accurate.

**Confirmed real issues:** 2 (both Important, non-blocking merge but should be fixed).

---

## Critical Issues — 0

None found.

---

## Important Issues — 2

### I1: `.Replace()` with no substring guard — silent transcript overwrite risk
- **File:** `MeetingsApiController.cs` line ~275
- **Category:** Correctness / Edge case
- **Issue:** If `TranscriptS3Key` does not contain the substring `"transcript.json"`, `string.Replace()` is a no-op and returns the original key unchanged. The summary is then uploaded to **the transcript's S3 key**, silently overwriting the transcript object in S3. `PutObjectAsync` does not error on overwrite. The try/catch does not protect against this — the write succeeds. This is a destructive silent failure mode.
- **Risk context:** The summary is already committed to DB before this runs, so the summary data is safe. But the transcript S3 object is destroyed. `GetTranscriptTextAsync` would then return summary text as if it were a transcript.
- **Evidence:**
  ```csharp
  var summaryS3Key = summaryMeeting.TranscriptS3Key.Replace("transcript.json", "summary.md");
  // If TranscriptS3Key = "recordings/abc/output.json":
  // summaryS3Key == "recordings/abc/output.json"  ← same as transcript key!
  ```
- **Fix:**
  ```csharp
  if (!summaryMeeting.TranscriptS3Key.Contains("transcript.json"))
  {
      _logger.LogWarning("FIRM: TranscriptS3Key does not contain 'transcript.json' for meeting {Id} — skipping summary S3 write", payload.MeetingId);
  }
  else
  {
      var summaryS3Key = summaryMeeting.TranscriptS3Key.Replace("transcript.json", "summary.md");
      try { ... }
      catch { ... }
  }
  ```

### I2: Redundant `FindAsync` on same EF context — code smell
- **File:** `MeetingsApiController.cs` lines ~175 and ~270
- **Category:** Quality / Maintainability
- **Issue:** `var meeting` is fetched at line 175 via `db.Meetings.FindAsync(payload.MeetingId)` on context `db`. `var summaryMeeting` is fetched at line 270 via the same `FindAsync` on the same `db` context. EF Core's change tracker returns the same tracked entity on the second call (no DB round-trip), but the different variable name obscures that `meeting` is already in scope and is exactly the entity needed. If the context is restructured, this assumption silently breaks.
- **Note:** Functionally correct today. `meeting` at line 175 has its `TranscriptS3Key` updated and saved at line 179/181 before the summary block, so the tracked entity is up to date. The behavior is correct, but the code is fragile and confusing.
- **Fix:** Remove `summaryMeeting`; reuse `meeting` with a null guard:
  ```csharp
  if (!string.IsNullOrEmpty(payload.Summary.SummaryText) &&
      meeting != null &&
      !string.IsNullOrEmpty(meeting.TranscriptS3Key))
  {
      var summaryS3Key = meeting.TranscriptS3Key.Replace("transcript.json", "summary.md");
  ```

---

## Nitpicks — 0

---

## What Passes ✅

| Check | Result |
|-------|--------|
| `UploadTextAsync` uses `PutObjectRequest.ContentBody` — correct AWS SDK v3 pattern | ✅ |
| `ContentType` `"text/markdown"` passed through correctly | ✅ |
| `UploadTextAsync` does not suppress exceptions (propagates to caller's try/catch) | ✅ |
| Bucket in `UploadTextAsync` matches bucket used by read methods in S3Service | ✅ |
| S3 write try/catch is non-fatal (Warning log, no rethrow) | ✅ |
| `return Ok()` is OUTSIDE and AFTER the try/catch block (line 348) | ✅ |
| Null guards (`summaryMeeting != null`, `!IsNullOrEmpty(TranscriptS3Key)`) before `.Replace()` | ✅ |
| Key convention in VpCallback matches DownloadSummary exactly | ✅ |
| S3 write is inside `summary_complete` branch guard | ✅ |
| `FirmKbService.PushSummaryAsync` reads from `db.Summaries` — DB, not S3 | ✅ |
| `FirmKbService.BuildSummaryContentAsync` reads from `db.Summaries` — DB, not S3 | ✅ |
| `FirmKbService` uses `IDbContextFactory<FirmDbContext>` pattern | ✅ |
| Scope: exactly 2 files modified | ✅ |

---

## What to Fix (NEEDS-CHANGES)

Tony needs to fix **2 items** before merge. Both are in the same S3-write block in `VpCallback`.

### Fix 1 — Required (I1): Add `.Contains()` guard before `.Replace()`

In `MeetingsApiController.cs`, inside the `summary_complete` block, wrap the `.Replace()` and S3 write:

```csharp
if (!summaryMeeting.TranscriptS3Key.Contains("transcript.json"))
{
    _logger.LogWarning("FIRM: TranscriptS3Key does not follow expected convention for meeting {Id} — skipping summary S3 write to avoid overwriting transcript", payload.MeetingId);
}
else
{
    var summaryS3Key = summaryMeeting.TranscriptS3Key.Replace("transcript.json", "summary.md");
    try
    {
        await _s3Service.UploadTextAsync(summaryS3Key, payload.Summary.SummaryText, "text/markdown");
        _logger.LogInformation("FIRM: Summary written to S3 for meeting {Id}: {Key}", payload.MeetingId, summaryS3Key);
    }
    catch (Exception s3Ex)
    {
        _logger.LogWarning(s3Ex, "FIRM: Failed to write summary to S3 for meeting {Id} (non-fatal, summary is in DB)", payload.MeetingId);
    }
}
```

### Fix 2 — Recommended (I2): Reuse `meeting` instead of second `FindAsync`

Replace `summaryMeeting` fetch with a reference to the already-tracked `meeting` entity:

```csharp
// Before (line ~270):
var summaryMeeting = await db.Meetings.FindAsync(payload.MeetingId);
if (!string.IsNullOrEmpty(payload.Summary.SummaryText) &&
    summaryMeeting != null &&
    !string.IsNullOrEmpty(summaryMeeting.TranscriptS3Key))
{
    var summaryS3Key = summaryMeeting.TranscriptS3Key.Replace("transcript.json", "summary.md");

// After:
if (!string.IsNullOrEmpty(payload.Summary.SummaryText) &&
    meeting != null &&
    !string.IsNullOrEmpty(meeting.TranscriptS3Key))
{
    var summaryS3Key = meeting.TranscriptS3Key.Replace("transcript.json", "summary.md");
```

---

## Positive Observations

- The non-fatal pattern is implemented correctly — summary is committed to DB before the S3 write, and the catch swallows the exception without affecting `return Ok()`. This is exactly right.
- `UploadTextAsync` is clean and minimal — single responsibility, no error suppression, correct SDK usage.
- `FirmKbService` correctly reads summary content from DB. The original bug claim (KB push couldn't find summary) was real and this confirms the fix path is sound.
- Key convention consistency between write and read paths is exact — no drift.

---

## Cycle 2 Review

**Verdict: PASS**
**Cycle:** 2
**Reviewer:** Hawkeye (Clint Barton)
**Commit:** `100575a`
**Date:** 2026-04-13

### Summary

Targeted verification of the two fixes from Cycle 1 (I1 + I2). Single file changed: `Controllers/MeetingsApiController.cs` (9 insertions, 4 deletions).

---

### Check 1: Guard uses `.Contains("transcript.json")` — ✅ PASS

**Evidence — line 274:**
```csharp
meeting.TranscriptS3Key.Contains("transcript.json"))
```
Exactly `.Contains`, not `EndsWith`, not regex. Comment on line 270 makes intent explicit. Correct choice for the naming convention.

---

### Check 2: S3 write inside if-block; LogWarning fires when guard fails; write is skipped — ✅ PASS

Structure confirmed (lines 271–291):
- `UploadTextAsync` at line 279 is inside the `if` block — only reachable when guard passes.
- The `else if` at line 288 contains **only** `LogWarning` — no S3 write, no path to `UploadTextAsync`.
- The two branches are mutually exclusive. The write is fully gated.

---

### Check 3: No remaining `summaryMeeting` references — ✅ PASS

Grep returned zero matches. All prior `summaryMeeting.TranscriptS3Key` references replaced with `meeting.TranscriptS3Key`. Clean.

---

### Check 4: `DownloadSummary` unguarded `.Replace()` — Acknowledged, pre-existing, not a blocker

Line 398 — pre-existing, out of scope for this WI:
```csharp
var summaryKey = meeting.TranscriptS3Key.Replace("transcript.json", "summary.md");
```
`DownloadSummary` guards on `!string.IsNullOrEmpty(meeting.TranscriptS3Key)` before this line, so no NRE risk. The no-op `.Replace()` scenario is the same pre-existing minor issue. Not flagged as a blocker.

---

### Minor Observation (non-blocking)

The `else if` warning condition does not re-check `!string.IsNullOrEmpty(meeting.TranscriptS3Key)`. If `TranscriptS3Key` is null/empty (vs. malformed), the warning fires and logs an empty key value. Message will read `...does not contain 'transcript.json': ` (blank). Cosmetically imprecise — safe, no S3 write, no exception. Not a blocker; worth a follow-up cleanup.

---

### Issues Resolved

| Issue | Status |
|-------|--------|
| I1: No guard before `.Replace()` — silent overwrite risk | ✅ Fixed |
| I2: Redundant `FindAsync` / confusing `summaryMeeting` variable | ✅ Fixed |

---

### Verdict: PASS

Both Cycle 1 issues correctly resolved. No new bugs introduced. Ships.

---

## Cycle 3 — SharePanel Direct Service Injection

**Verdict: NEEDS-CHANGES**
**Cycle:** 3
**Reviewer:** Hawkeye (Clint Barton)
**Commit:** `0edf3b1`
**Date:** 2026-04-13
**Risk:** Medium (Blazor component refactor, new service methods)

---

### Spec Compliance Check

**Scope:** `SharePanel.razor` + `FirmBotService.cs` (contains `IFirmBotService`). No other files.

**git show 0edf3b1 --stat (application files):**
- `Components/Pages/SharePanel.razor` — 146 lines changed ✅
- `Services/FirmBotService.cs` — 57 lines added ✅

✅ Scope compliant. Only the expected files modified.

---

### Consistency Audit

**HttpClientFactory elimination:**
- `grep HttpClientFactory SharePanel.razor` → zero results ✅

**IFirmBotService interface vs concrete:**
- `GetChannelPostHistoryAsync(long meetingId)` → interface ✅, concrete ✅, signatures match ✅
- `PostMeetingToChannelAsync(long meetingId, Guid initiatedByUserId, string teamId, string teamName, string channelId, string channelName, string docType)` → interface ✅, concrete ✅, signatures match ✅

**PushDocumentAsync call mapping:**
- Signature: `(long meetingId, string userId, string faitUserId, string docType, IEnumerable<string> kbScopes)`
- Actual call: `(MeetingId, _user.Id.ToString(), _user.FaitUserId, docType, kbScopes)` ✅

**SQL column aliases vs projection type:**
- `ChannelPostHistoryRow2` fields: `TeamName`, `ChannelName`, `DocType`, `PostedAt`, `Success`
- SQL aliases: `team_name AS TeamName`, `channel_name AS ChannelName`, `doc_type AS DocType`, `posted_at AS PostedAt`, `success AS Success` ✅

---

### CC Review Summary

CC ran full adversarial review of all changed files plus `Program.cs`, `MeetingDetail.razor`, and `FirmKbService.cs` for reference.

**Dismissed as false positives:** None. Both CC findings are confirmed real issues.

**Confirmed real issues:** 2 (one Critical, one Important).

---

### Critical Issues — 1

#### C1: Singleton `FirmBotService` Captures Scoped `S3Service` — Captive Dependency

- **File:** `Program.cs` (lines 77 + 85) + `Services/FirmBotService.cs` (constructor)
- **Category:** Correctness / Lifetime
- **Issue:** `FirmBotService` is registered as `AddSingleton<IFirmBotService, FirmBotService>()`. It now injects `S3Service` in its constructor. `S3Service` is registered as `AddScoped<S3Service>()`. This is a classic captive dependency — the root DI container must resolve `S3Service` to construct the Singleton, which creates a root-scope instance of `S3Service` that is never properly lifecycle-managed per circuit.
- **Evidence:**
  ```csharp
  // Program.cs line 77
  builder.Services.AddScoped<S3Service>();
  // Program.cs line 85
  builder.Services.AddSingleton<IFirmBotService, FirmBotService>();

  // FirmBotService constructor
  public FirmBotService(
      IDbContextFactory<FirmDbContext> dbFactory,
      IBotFrameworkHttpAdapter adapter,
      IConfiguration config,
      S3Service s3Service,   // ← Scoped captured by Singleton
      ILogger<FirmBotService> logger)
  ```
- **Impact:** In dev (`ValidateScopes = true`): throws `InvalidOperationException: Cannot consume scoped service 'S3Service' from singleton 'IFirmBotService'` at startup. In prod (`ValidateScopes = false`): one `S3Service` instance lives in the root scope for the process lifetime — but since `S3Service` only wraps `IAmazonS3 + IConfiguration + ILogger` (all Singletons/safe), runtime behavior will be correct in practice. Still a DI contract violation that must be fixed — if `S3Service` ever gains per-request state this will become a live bug immediately.
- **Fix:** Change `S3Service` registration to `AddSingleton` in `Program.cs`:
  ```diff
  - builder.Services.AddScoped<S3Service>();
  + builder.Services.AddSingleton<S3Service>();
  ```
  `S3Service` only injects `IAmazonS3` (Singleton via `AddAWSService`), `IConfiguration` (Singleton), and `ILogger` (Singleton) — there is no per-request state, so Singleton lifetime is correct per MEMORY.md (AWS SDK clients must be Singleton). This is a one-line fix.

---

### Important Issues — 1

#### I1: `channelName` Always Recorded as `""` — History Display Permanently Broken

- **File:** `Components/Pages/SharePanel.razor` (PostToChannels, ~line 246)
- **Category:** Correctness / Data loss
- **Issue:** `PostMeetingToChannelAsync` receives `channelName` as `""` hardcoded because `ChannelRowState` has no `ChannelName` property (channels come from Graph — now empty). The `firm_meeting_channel_posts` table stores `channel_name = ""` for every row inserted through this path. History display in the component renders `h.ChannelName` — which will always be blank.
- **Evidence:**
  ```csharp
  // ChannelRowState (line 295) — no ChannelName field
  private class ChannelRowState
  {
      public string? TeamId { get; set; }
      public string? ChannelId { get; set; }
      public string DocType { get; set; } = "summary";
      public List<ChannelItem> Channels { get; set; } = new();
      // ← No ChannelName
  }

  // PostToChannels (~line 246)
  await BotService.PostMeetingToChannelAsync(
      MeetingId, _user.Id,
      row.TeamId ?? "",
      team?.DisplayName ?? "",
      row.ChannelId ?? "",
      "",          // ← channelName always ""
      docType);
  ```
- **Impact:** Post history shows blank channel names permanently. Any future channel name lookup from the history record (e.g., for display or audit) will have missing data that cannot be reconstructed.
- **Fix:** Add `ChannelName` to `ChannelRowState`, and populate it from the `ChannelItem` when a channel is selected in `OnTeamSelected`. When channel list is empty (current state), default to `""` is acceptable — but structure should be in place:
  ```csharp
  private class ChannelRowState
  {
      public string? TeamId { get; set; }
      public string? ChannelId { get; set; }
      public string? ChannelName { get; set; }  // ← add this
      // ...
  }
  ```
  In OnTeamSelected (or when user selects a channel): `row.ChannelName = selectedChannel?.DisplayName`.

---

### Nitpicks — 1

- **N1:** `LoadKbRows()` and `LoadTeams()` return `Task.CompletedTask` but are called with `await` in `OnInitializedAsync`. Minor: could be `void` methods since they're synchronous, but `Task` return is fine — Blazor can await them cleanly and it keeps the signature consistent with future async expansion. Not blocking.

---

### Positive Observations

- ✅ `faitUserId` null guard is excellent — actionable message, early return, correct placement before the service loop.
- ✅ `_user == null` guards on both `PushToKb` and `PostToChannels` — consistent and correct.
- ✅ Per-row try/catch in `PushToKb` loop is a good pattern — one KB row failure doesn't kill the entire batch.
- ✅ `GetChannelPostHistoryAsync` is parameterized SQL (no injection risk), aliases match projection type exactly.
- ✅ User loading pattern matches `MeetingDetail.razor` exactly — same claim names, same order.
- ✅ `OnTeamSelected` simplified to synchronous correctly (no channels to load from Graph anymore).

---

### Acceptance Criteria Verification

- [x] **Zero HttpClientFactory.CreateClient("local")** — ✅ Verified clean
- [x] **PushDocumentAsync correct parameters** — ✅ All 5 params correct
- [x] **faitUserId null guard** — ✅ Guard present with actionable message
- [x] **IFirmBotService interface updated** — ✅ Both methods added with matching signatures
- [x] **No crash on empty dropdowns** — ✅ All null-coalesced correctly
- [ ] **DI lifetimes correct** — ❌ Singleton/Scoped mismatch (C1)

---

### What to Fix

**C1 (Required):** In `Program.cs`, change line 77:
```diff
- builder.Services.AddScoped<S3Service>();
+ builder.Services.AddSingleton<S3Service>();
```
`S3Service` has no per-circuit state. All its dependencies are Singleton-safe. One-line fix.

**I1 (Should fix):** Add `ChannelName` property to `ChannelRowState`. Pass it through to `PostMeetingToChannelAsync`. Currently always `""` — history records will be missing channel name data.

