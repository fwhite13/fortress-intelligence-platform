# BUILD Report — ADO #1957
**Agent:** Tony Stark (BUILD cycle 1)
**Date:** 2026-04-15
**Commit:** `50dafcf`

---

## Changes Made

### Issue 1 — Vision prompt leakage (FIXED)
`nexus/src/FortressNexus.Web/Services/Discovery/DiscoveryService.cs`

- Removed `submission.Title` from both prompt branches (was causing question-generation bleed)
- Replaced with explicit UI description instructions
- Added "Do not generate questions or recommendations." to both branches
- Preserved `file.UserDescription` in the with-description branch

### Issue 2 — Question count limit (FIXED)
`nexus/src/FortressNexus.Web/appsettings.json`

- `DiscoveryQuestionGen` prompt: `3-7 questions` → `up to 10 questions`

### Issue 3 — Image description logging (FIXED)
`nexus/src/FortressNexus.Web/Services/Discovery/DiscoveryService.cs`

- Added `[DISCOVERY_GEN] Image description for {FileName} (attempt {Attempt}): {Description}` log immediately after successful vision call

### Issue 4 — Duplicate log entries (FIXED)
`nexus/src/FortressNexus.Web/Program.cs`

- Added `builder.Logging.ClearProviders()` before `builder.Host.UseSerilog(...)` (Option A)
- Root cause: `WebApplication.CreateBuilder` registers the default `Microsoft.Extensions.Logging` console provider; `UseSerilog` on `builder.Host` replaces the host-level providers but the default console provider registered via `builder.Logging` was still writing to stdout independently, resulting in awslogs capturing both streams.
- `ClearProviders()` explicitly removes all default logging providers before Serilog takes over.

---

## Build Result

```
1 Warning(s)
0 Error(s)
Time Elapsed 00:00:04.68
```

**Status: SUCCEEDED**

---

## ADO Comment

```
mcporter call devops.add_comment project="FAIT" id=1957 text="**[Tony Stark — BUILD cycle 1]**\nCommit 50dafcf: vision prompt leakage fixed (removed title injection, added no-questions instruction), question count 3-7→up to 10, image description logging added, duplicate log fix (ClearProviders). Build: SUCCEEDED."
```
