## Build Report — ADO #1712 (Download missing sections)

### What was built
Fixed summary download to always include structured sections (decisions, action items, follow-ups) after the overview text.

### Root Cause
In `DownloadSummary` (DB fallback path), structured sections (KeyDecisions, ActionItems, FollowUps) were inside an `else` branch that only ran when `SummaryText` was empty. Since `SummaryText` is always populated (it contains the full meeting markdown), the else branch never executed. Download only contained the `SummaryText` overview.

**Secondary bug:** `ActionItem` class used for deserialization had no `[JsonPropertyName]` attributes, so `Owner`, `Description`, and `Deadline` would be null even in the else path.

### Files changed
- `Controllers/MeetingsApiController.cs`:
  - `DownloadSummary`: Removed `else` — structured sections now ALWAYS appended after `SummaryText`. Added null/empty guards and try/catch per section.
  - `ActionItem` class: Added `[JsonPropertyName]` attributes for `description`, `owner`, `deadline`

### Build result
✅ 0 errors

### How to test
1. Download summary for a completed meeting
2. Confirm the `.md` file contains Overview + Decisions Made + Action Items + Follow-ups sections
3. Confirm action items show owner and deadline correctly

### Known edge cases / things Clint should scrutinize
- S3 path is unchanged — if S3 `summary.md` only has overview text, the S3 path still returns that. This is a known limitation and a separate concern from the DB path fix in this WI.
- Try/catch per structured section means a malformed JSON in one section won't break the entire download
