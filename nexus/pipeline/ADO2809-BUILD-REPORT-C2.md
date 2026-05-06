# Build Report — ADO#2809 — Cycle 2

## What was built
Null guard on `GetManifestResourceStream` in `DatabaseInitializationService.StartAsync`. If the embedded resource `forge-kb-spec-seed.md` is not found at runtime, the FORGE KB seed is now skipped with a `LogError` instead of crashing with a `NullReferenceException`.

## Files changed
- `src/FortressNexus.Web/Services/DatabaseInitializationService.cs` — Wrapped the FORGE KB seed body in `if (stream is null) { LogError } else { ... }`. Removed the null-forgiving `stream!` dereference.

## Commit
`1429f04` — `fix(ADO#2809): null guard on embedded resource stream in FORGE KB seed`

## Parallelization used
No — single-file change, sequential.

## CC sessions run
1 — CC Sonnet, pipe mode. Build verified within CC run.

## Acceptance criteria verification
- [x] `stream!` null-forgiving dereference removed
- [x] `LogError` emitted when stream is null with message `[NEXUS] forge-kb-spec-seed.md embedded resource not found — FORGE KB seed skipped.`
- [x] Rest of seed block (NexusAdmin + subsequent FORGE KB logic) unaffected
- [x] `dotnet build` passes (Release config, no restore)

## Known edge cases / things Clint should scrutinize
- The `ReadToEndAsync()` call inside the `else` block does not pass `cancellationToken` — this matches the original code intentionally (leave as-is unless you want to add it)
- If the embedded resource IS present, behavior is 100% unchanged from before

## How to test locally
```bash
cd /home/fredw/projects/fip/nexus
dotnet build src/FortressNexus.Web/FortressNexus.Web.csproj -c Release
# Remove/rename forge-kb-spec-seed.md from Resources and run locally to verify LogError appears and app doesn't crash
```
