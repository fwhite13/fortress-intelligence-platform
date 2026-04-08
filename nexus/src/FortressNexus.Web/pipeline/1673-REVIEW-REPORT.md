# Review Report — WI #1673 — Stale Bedrock Model ID Fix

**Reviewer:** Hawkeye (Clint Barton)
**Date:** 2026-04-08
**Commit:** `60beea1`
**Cycle:** 1
**Risk:** Low (config-only change)

---

## Verdict: ✅ PASS

Fix is correct and complete. Config aligned, scope clean, build confirmed.
One non-blocking follow-up recommended.

---

## Spec Compliance Check

**Claimed change:** Update stale `us.anthropic.claude-3-5-sonnet-20241022-v2:0` to `us.anthropic.claude-sonnet-4-6` in `appsettings.json`.

**§ Files changed:**
- `nexus/src/FortressNexus.Web/appsettings.json` — ✅ only file in diff (2 ins, 2 del)

**§ Out of scope:**
- ✅ No `.cs`, `.razor`, or logic files touched

**Spec compliance verdict:** ✅ COMPLIANT

---

## Consistency Audit

| Check | Result |
|---|---|
| `appsettings.json` — `Bedrock:DiscoveryModelId` | ✅ `us.anthropic.claude-sonnet-4-6` |
| `appsettings.json` — `Bedrock:Discovery:ModelId` | ✅ `us.anthropic.claude-sonnet-4-6` |
| `appsettings.Production.json` — Bedrock section | ✅ Absent — inherits from base config |
| `BedrockService.cs:16` — `DefaultModelId` const | ✅ `us.anthropic.claude-sonnet-4-6` — matches |
| Old model ID `claude-3-5-sonnet-20241022-v2:0` in any runtime config | ✅ Absent |
| `DiscoveryInferenceConfig.cs:9` — C# property default | ⚠️ Still `claude-3-5-sonnet-20241022-v2:0` (stale, non-blocking — see below) |

**Config binding chain confirmed:**
`Program.cs:140-141` → `builder.Services.Configure<DiscoveryInferenceConfig>(builder.Configuration.GetSection("Bedrock:Discovery"))` → overrides the C# default at startup with the new model ID from appsettings.json. Runtime behavior is correct.

---

## Issues Found

### Critical Issues: 0

### Important Issues: 1

#### I1: `DiscoveryInferenceConfig.cs` — stale C# property default

- **File:** `Services/Discovery/DiscoveryInferenceConfig.cs` line 9
- **Category:** Consistency / maintenance hazard
- **Issue:** Property initializer still references the old retired model ID. Config binding overrides this at runtime, so there is **no current regression** — but:
  - Misleads any developer reading the class
  - Unit tests instantiating `DiscoveryInferenceConfig` directly (without DI) would silently use the stale broken model
  - Risk grows if the class is ever constructed outside the DI/config pipeline
- **Fix:**
  ```diff
  - public string ModelId { get; set; } = "us.anthropic.claude-3-5-sonnet-20241022-v2:0";
  + public string ModelId { get; set; } = "us.anthropic.claude-sonnet-4-6";
  ```
- **Blocking?** No — does not block PASS. Should be cleaned up in a follow-up commit.

### Nitpicks: 0

---

## Positive Observations

- Both config keys updated in a single atomic commit — no partial fix risk
- `BedrockService.DefaultModelId` was already correct; Tony correctly aligned config to it
- `appsettings.Production.json` correctly omits Bedrock overrides — clean inheritance

---

## CC Review Summary

CC confirmed all pre-verified facts independently. CC assessed `DiscoveryInferenceConfig.cs` stale default as **Important (non-blocking)** — runtime behavior correct due to config binding, but maintenance hazard is real. CC found no additional runtime issues. Old model ID appears only in pipeline artifacts and KB seed docs (neither runtime concern).

---

## Build

`dotnet build` — 0 errors (config-only change, no C# modification)

---

## Follow-up Ticket Recommended

Update `DiscoveryInferenceConfig.cs:9` default to `us.anthropic.claude-sonnet-4-6` — can be bundled into any nearby commit or addressed as a 5-minute cleanup task.
