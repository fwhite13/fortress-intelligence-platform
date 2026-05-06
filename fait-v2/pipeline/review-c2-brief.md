# Hawkeye — Cycle 2 Review Brief — ADO#2842

You are performing a cycle 2 code review for ADO#2842 (FAIT v2 Blazor Server app shell).
Commit: `8362cdf` — fixes from cycle 1.

## Primary Focus: Verify Two Fixes

### Fix I1 — `Components/Pages/Onboarding.razor`
Read the file at: `src/FortressAI.V2.Web/Components/Pages/Onboarding.razor`
VERIFY: `@attribute [Authorize]` is present (should be line 2, directly after `@page "/onboarding"`)
VERIFY: No other changes were made to this file beyond adding that one attribute

### Fix I2 — `Program.cs`
Read the file at: `src/FortressAI.V2.Web/Program.cs`
VERIFY the exact middleware ordering:
1. Security headers `app.Use(async (context, next) => { ... })` — MUST be FIRST
2. `app.UseStaticFiles()` — MUST come AFTER security headers
3. Then: routing → authentication → authorization → antiforgery → endpoints

Look for: app.UseStaticFiles(), app.UseRouting(), app.UseAuthentication(), app.UseAuthorization(), app.UseAntiforgery(), app.MapRazorComponents
CONFIRM: security headers block appears BEFORE UseStaticFiles line number-wise.
REPORT: the exact line numbers of app.Use (security headers start), app.UseStaticFiles(), app.UseRouting(), app.UseAuthentication(), app.UseAuthorization(), app.UseAntiforgery(), app.MapRazorComponents()

## Secondary Check: All 6 Pages Have [Authorize]

Read all 6 page files:
- `src/FortressAI.V2.Web/Components/Pages/Dashboard.razor`
- `src/FortressAI.V2.Web/Components/Pages/Onboarding.razor`
- `src/FortressAI.V2.Web/Components/Pages/Memory.razor`
- `src/FortressAI.V2.Web/Components/Pages/Tasks.razor`
- `src/FortressAI.V2.Web/Components/Pages/Workspace.razor`
- `src/FortressAI.V2.Web/Components/Pages/Connectors.razor`

For each file: confirm `@attribute [Authorize]` is present and report YES/NO per file.

## Scope Creep Check

Run: `git diff HEAD~1 --name-only` or `git show --stat 8362cdf`
CONFIRM: Only these 2 files were changed:
- `src/FortressAI.V2.Web/Components/Pages/Onboarding.razor`
- `src/FortressAI.V2.Web/Program.cs`

If ANY other files were modified, list them — that is scope creep and must be flagged.

## New Issues Check

While reading Program.cs and Onboarding.razor, scan for any newly introduced issues:
- Syntax errors
- Logic errors introduced by the reorder (e.g., did the security headers middleware accidentally wrap the wrong block?)
- Any TODO, debug code, or accidental deletions

Report each file's full content briefly so I can confirm the context.

## Output Format

For each check: ✅ PASS or ❌ FAIL with evidence (line numbers, code snippet).
End with a summary: PASS (both fixes confirmed, no new issues, no scope creep) or NEEDS-CHANGES / FAIL with specifics.
