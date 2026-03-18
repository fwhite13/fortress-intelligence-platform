# Review Brief: WI832 — Cowork Sprint 1 — Cycle 2 of 2

You are Hawkeye (Clint Barton), code reviewer. This is a focused C2 re-check.

## Context

C1 returned NEEDS-CHANGES with 2 required fixes. C2 commit is `a2b3089`.
Repo: `/home/fredw/projects/fip/`

Fred explicitly approved `bash` in allowedTools — do NOT flag that.

## Pre-Verified Checks (already confirmed via grep before this CC run)

**Fix 1 — .NET 9 alignment:**
- `cowork/Dockerfile.web`: `FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build` and `FROM mcr.microsoft.com/dotnet/aspnet:9.0` ✅
- `cowork/src/CoworkWeb/CoworkWeb.csproj`: `<TargetFramework>net9.0</TargetFramework>` ✅

**Fix 2 — SSE close handler in `cowork/src/CoworkAgent/src/routes/tasks.ts`:**
- Line 76: `let cancelled = false;` ✅
- Lines 77-78: `req.on('close', () => { cancelled = true; })` ✅
- Line 83: `if (cancelled) break;` inside the loop ✅

**Regression checks — C1 PASS items:**
- `Program.cs` lines 69-70: `SetApplicationName("FortressAI")` + `.DisableAutomaticKeyGeneration()` ✅
- `TaskPage.razor` line 57: `sandbox="allow-scripts"` ✅
- `auth.ts` lines 4-5: `COWORK_INTERNAL_SECRET` env var with fail-fast throw ✅

## Your Task

Review the C2 changes in the repo at `/home/fredw/projects/fip/cowork/` to:

1. Confirm the two C1 fixes are properly implemented (evidence above)
2. Do a quick scan of the SSE handler context (lines 70-100 of tasks.ts) to confirm the close handler and break are correctly positioned within the stream loop — not just present as dead code
3. Confirm no regressions were introduced in the fix commits
4. Check that the .NET 9 change in csproj didn't introduce any version mismatches elsewhere (e.g., any other `.csproj` files, global.json, or docker-compose referencing a dotnet version)

## Files to Check

- `cowork/src/CoworkAgent/src/routes/tasks.ts` — SSE handler (lines 70-100)
- `cowork/src/CoworkWeb/CoworkWeb.csproj` — net9.0 target
- `cowork/Dockerfile.web` — sdk:9.0 / aspnet:9.0
- `cowork/src/CoworkWeb/Program.cs` — regression check
- Any other .csproj or global.json files in cowork/

## Deliverable

Return a structured verdict with:
- Whether both C1 fixes are correctly implemented (not just superficially present)
- Whether any regressions exist
- Whether any new issues were introduced
- Final verdict: PASS or NEEDS-CHANGES (with specific issues if NEEDS-CHANGES)
