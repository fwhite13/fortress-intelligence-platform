# Review Report: ADO#2880 — FAIT v2 Marketing Agent Seed

**Reviewer:** Hawkeye (Clint Barton)
**Date:** 2026-05-07
**Commit:** `2324abe`
**Verdict:** ⚠️ **NEEDS-CHANGES**

---

## Spec Compliance Check

**Files reviewed:**
- `wwwroot/claude/agents/marketing.md` — ✅ present
- `wwwroot/claude/agents/finance.md` — ✅ present
- `wwwroot/claude/agents/legal.md` — ✅ present
- `Data/Migrations/20260507210000_SeedInitialAgentPlugins.cs` — ✅ present
- `Services/PluginAgentService.cs` — ✅ modified (IWebHostEnvironment injection + GetSkillsContentAsync)

**Out of Scope:** No out-of-scope changes detected.

---

## Check Results

| # | Check | Result |
|---|-------|--------|
| 1 | Migration uses `InsertData()` only — no raw SQL | ✅ PASS |
| 2 | `Down()` deletes seeded rows by ID | ✅ PASS |
| 3 | Seed GUIDs are fixed/deterministic (not `Guid.NewGuid()`) | ✅ PASS — `00000000-0000-0000-0000-00000000000{1,2,3}` |
| 4 | `AllowedRoles` is `"[]"` for all 3 agents | ✅ PASS |
| 5 | `AllowedMcpServers` is `"[]"` for all 3 agents | ✅ PASS |
| 6 | `IWebHostEnvironment` injected in constructor | ✅ PASS (`PluginAgentService.cs:17`) |
| 7 | `GetSkillsContentAsync`: `wwwroot/` prefix → `WebRootPath` resolution | ✅ PASS (`PluginAgentService.cs:90-98`) |
| 8 | `File.Exists` check with graceful fallback + warning log | ✅ PASS |
| 9 | Defensive null guard on `_env.WebRootPath` | ❌ NEEDS-CHANGES |
| 10 | `dotnet build` — 0 errors | ✅ PASS |
| 11 | No Cognito references | ✅ PASS |

---

## Critical Issues — 0

---

## Important Issues — 1

### I1: `WebRootPath` null guard missing (`PluginAgentService.cs` ~line 93)

- **File:** `Services/PluginAgentService.cs` (~lines 90–98)
- **Category:** Correctness / Defensive coding
- **Issue:** `Path.Combine(_env.WebRootPath, ...)` is called without a null check. `IWebHostEnvironment.WebRootPath` can be null in non-web test hosts or custom host configurations, causing an `ArgumentNullException` at runtime.
- **Impact:** Service crashes on `GetSkillsContentAsync` in any host context where `WebRootPath` is not set (e.g., unit tests, worker host).
- **Fix:**

```diff
 if (plugin.SkillsDirectory.StartsWith("wwwroot/"))
 {
+    if (string.IsNullOrEmpty(_env.WebRootPath))
+    {
+        _logger.LogWarning("WebRootPath is null; cannot resolve skills file for plugin {Name}", plugin.Name);
+        return string.Empty;
+    }
     var filePath = Path.Combine(_env.WebRootPath,
         plugin.SkillsDirectory["wwwroot/".Length..]);
```

---

## Nitpicks — 0

---

## Positive Observations

- Clean migration design: `InsertData()` / `DeleteData()` pattern, no raw SQL.
- Fixed/deterministic GUIDs (`00000000-...-001/002/003`) — idempotent migrations done right.
- `AllowedRoles` and `AllowedMcpServers` both `"[]"` — correct open-access seed posture.
- `IWebHostEnvironment` DI injection is clean and correct.
- `File.Exists` fallback pattern (return `string.Empty` + log warning) is exactly right.
- Build is clean — 0 errors.

---

## What to Fix

**Tony:** One fix needed before this merges.

In `Services/PluginAgentService.cs`, inside `GetSkillsContentAsync`, add a null guard for `_env.WebRootPath` before the `Path.Combine` call (see diff above in I1). Without it, running this in any non-web host context (test runner, integration tests) will throw `ArgumentNullException`.

Everything else is solid. Fix I1 and resubmit.
