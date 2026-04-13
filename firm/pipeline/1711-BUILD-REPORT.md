## Build Report — ADO #1711 (Action items not rendering)

### What was built
Fixed camelCase/PascalCase JSON deserialization mismatch that prevented action items from rendering in MeetingDetail.razor.

### Root Cause
`System.Text.Json` is case-sensitive by default. The AI summarizer produces JSON with camelCase keys (`"description"`, `"owner"`, `"deadline"`) but `ActionItemDisplay` record had PascalCase properties with no `[JsonPropertyName]` attributes, so deserialization silently returned null values.

### Files changed
- `Components/Pages/MeetingDetail.razor` — Added `[JsonPropertyName]` attributes to `ActionItemDisplay` record; added `"deadline"` field; added `using System.Text.Json.Serialization` if not already present

### Additional findings
- `KeyDecisionsJson` and `FollowUpsJson` use `List<string>` deserialization — not affected by this bug
- `quotesJson` is NOT a separate AI output field; quotes are embedded in `summaryText` markdown. No `QuotesJson` property needed. TODO comment added to `TeamsGraphService.cs`

### Build result
✅ 0 errors

### How to test
1. Open a completed meeting with action items in FIRM
2. Confirm action items render with owner + description in the Summary tab
3. Action items with no owner should show description only

### Known edge cases / things Clint should scrutinize
- `ActionItemDisplay` now has a `Deadline` field added (was missing before) — it's not currently rendered in the UI, but it's bound correctly for future use
