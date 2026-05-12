# Build Report — ADO#3300

## What was built
Fixed two bugs in KbFlags construction in `ChatView.razor`:
1. `TeamKbEnabled` was incorrectly set `true` when a project conversation existed but no Team KB was selected — this caused the harness to receive `TeamKbEnabled: true` with `TeamIds: null` and skip team KB retrieval with a security warning.
2. Added `Session.UserId != Guid.Empty` guard on `PersonalKbUserId` to prevent sending an all-zeros GUID when the session isn't fully hydrated.

## Files changed
- `src/FortressAI.Web/Components/Chat/ChatView.razor` — KbFlags construction in `HandleSend`

## Changes
```diff
-  Console.WriteLine($"[KB] Flags — Corp:{hasCorpKb} Personal:{hasPersonalKb} Team:{hasTeamKb} Project:{hasProjectKb}");
+  Console.WriteLine($"[KB] Flags — Corp:{hasCorpKb} Personal:{hasPersonalKb} Team:{hasTeamKb} Project:{hasProjectKb} UserId:{Session.UserId}");

-  TeamKbEnabled: hasTeamKb || hasProjectKb,
-  PersonalKbUserId: hasPersonalKb ? Session.UserId.ToString() : null,
+  TeamKbEnabled: hasTeamKb,
+  PersonalKbUserId: hasPersonalKb && Session.UserId != Guid.Empty
+      ? Session.UserId.ToString()
+      : null,
```

## Root cause
- **teamKbEnabled bug**: `TeamKbEnabled: hasTeamKb || hasProjectKb` was a mistaken coupling. Project KB drives `anyKbActive` (so a harness turn is made) but doesn't map to team KB retrieval. When user has a project conversation but no team KB enabled, harness would receive `TeamKbEnabled: true, TeamIds: null` → "no TeamIds — skipping for security" warning.
- **PersonalKbUserId bug**: No guard against `Guid.Empty` — if session wasn't fully hydrated, the empty GUID would propagate.

## Parallelization used
No — single file change.

## CC sessions run
1 CC run (sonnet).

## Acceptance criteria verification
- [x] `TeamKbEnabled` is `true` only when `_selectedTeamIds.Any() == true` — verified in diff
- [x] `PersonalKbUserId` is null when `Session.UserId == Guid.Empty` — verified in diff
- [x] `hasProjectKb` remains in `anyKbActive` — unchanged
- [x] `dotnet build` → 0 errors — verified

## Commit
`2c7a7937` — fix(fait#3300): KbFlags — TeamKbEnabled only on team selections, PersonalKbUserId Guid.Empty guard

## Known edge cases / things Clint should scrutinize
- `TeamKbEnabled` no longer fires for project conversations. If there's a future requirement for project-level team KB (where the project is associated with a team), this would need revisiting. For now, project KB is strictly project-scoped via system prompt, not team KB.
- The `Guid.Empty` guard on PersonalKbUserId means if the session service is broken and returns empty GUID, personal KB is silently skipped rather than querying with a bad filter. This is the desired security behavior per the existing harness pattern.

## How to test locally
1. Enable Personal KB in chat — verify `[KB] Flags` log shows `Personal:True UserId:<real-guid>`
2. Enable Team KB — verify `TeamKbEnabled: true` in the harness `/turn` log
3. Open a project conversation with no team KB selected — verify `teamKbEnabled: false` in harness (no security skip warning)
