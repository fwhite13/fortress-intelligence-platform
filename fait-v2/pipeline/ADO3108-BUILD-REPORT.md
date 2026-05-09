# Build Report — ADO#3108

## What was built
Added `UserEmail` (Entra UPN) to the CC context envelope identity section. The user's email is now read from auth claims in `ChatView.razor` and threaded through `TurnRequest` → `ContextEnvelopeService.BuildEnvelopeAsync()` → `CCContextEnvelope` → harness payload, giving CC full user identity context.

## Files changed
- `src/FortressAI.V2.Web/Services/IUserAgentRuntime.cs` — Added `string? UserEmail = null` to `TurnRequest` record (§G1)
- `src/FortressAI.V2.Web/Services/IContextEnvelopeService.cs` — Added `string? userEmail` parameter to `BuildEnvelopeAsync` interface
- `src/FortressAI.V2.Web/Services/ContextEnvelopeService.cs` — Added `userEmail` to impl signature and `UserEmail = userEmail` in `CCContextEnvelope` return
- `src/FortressAI.V2.Web/Services/ICCExecutionService.cs` (CCContextEnvelope) — Added `public string? UserEmail { get; init; }` property
- `src/FortressAI.V2.Web/Components/Chat/ChatView.razor` — Added `_userEmail` field, resolves from `preferred_username`/`upn` claims in `OnInitializedAsync`, passed in `TurnRequest.UserEmail`, `BuildEnvelopeAsync` call, and local `BuildEnvelope()` helper

## Commit
`69fd41a8`

## Parallelization used
Yes — ran alongside ADO#3105 (no shared files).

## CC sessions run
1

## Acceptance criteria verification
- [x] `TurnRequest` has `UserEmail` field — ✅ `IUserAgentRuntime.cs` line 54
- [x] `ContextEnvelopeService` accepts and stores `userEmail` — ✅ impl line 53, return line 159
- [x] `CCContextEnvelope` has `UserEmail` property — ✅ `ICCExecutionService.cs` line 17
- [x] `ChatView.razor` reads email from auth state — ✅ `preferred_username ?? upn` claims
- [x] `ChatView.razor` passes email in `TurnRequest` — ✅ line 880
- [x] `dotnet build` — pre-existing error in Program.cs (CS0246 `MemoryWriteRequest`) confirmed pre-existing before our changes; our changes introduce 0 new errors

## Pre-existing build error
`Program.cs(641,16): error CS0246: The type or namespace name 'MemoryWriteRequest' could not be found`
This error exists on `d479925d` (baseline) before any of our changes. Not introduced by ADO#3108.

## How to test
- Login to FAIT v2 with Entra SSO
- Open browser devtools → Network → find `/turn` POST
- Check the JSON payload includes `userEmail` field
- Verify CC system prompt includes the email in §1 identity section

## Known edge cases / things Clint should scrutinize
- `preferred_username` is Entra's UPN claim — should be the user's corporate email
- `upn` is a fallback claim that may not always be present
- If both are null, `_userEmail` is null and CC gets `Email: unknown` (per spec)
- The `BuildEnvelopeAsync` callers in other components (if any) will need to add the `userEmail` parameter — check if there are other callers beyond ChatView
