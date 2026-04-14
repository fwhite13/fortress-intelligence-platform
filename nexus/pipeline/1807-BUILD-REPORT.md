# Build Report — ADO #1807
## SpecGen model IDs to config (IOptions pattern)

**Commit:** `71aead9`  
**Branch:** `main`  
**Build:** ✅ 0 errors, 0 warnings  
**Date:** 2026-04-13

---

### What was built

Moved `BedrockService.DefaultModelId` and all hardcoded model IDs in the spec generation pipeline out of code and into configuration using the `IOptions<SpecGenInferenceConfig>` pattern. Parallel to existing `DiscoveryInferenceConfig`. Model IDs, max tokens, and vision timeout are now overridable via config/env vars without a code deploy.

---

### Files changed

| File | Change |
|------|--------|
| `Services/SpecGenInferenceConfig.cs` | **Created** — new config class bound to `Bedrock:SpecGen` section |
| `Program.cs` | Added `Configure<SpecGenInferenceConfig>` registration after Discovery config |
| `appsettings.json` | Added `Bedrock:SpecGen` section with all 5 config keys |
| `Services/BedrockService.cs` | Removed `DefaultModelId` const; both `InvokeAsync` and `InvokeWithImageAsync` now require `string modelId` (no default, no null fallback) |
| `Services/SpecGenerationService.cs` | Injects `IOptions<SpecGenInferenceConfig>`; uses `_specGenConfig.ModelId`, `_specGenConfig.VisionModelId`, `_specGenConfig.MaxTokens`, `_specGenConfig.VisionMaxTokens`, `_specGenConfig.TimeoutSeconds` |
| `Services/ArtifactGenerationService.cs` | `modelId` null-coalesced to `"us.anthropic.claude-sonnet-4-6"` fallback since `modelId` no longer optional on `BedrockService` |

---

### Parallelization used
No — single CC session, sequential changes (BedrockService signature change must complete before callers are fixed).

### CC sessions run
1 — `claude --model sonnet --print --dangerously-skip-permissions`

---

### Acceptance criteria verification

- [x] `Services/SpecGenInferenceConfig.cs` exists with correct namespace and 5 properties
- [x] `Program.cs` has `Configure<SpecGenInferenceConfig>` registration
- [x] `appsettings.json` has `Bedrock:SpecGen` section
- [x] `BedrockService.cs` — `DefaultModelId` removed, `modelId` required on both methods
- [x] `SpecGenerationService.cs` — injects `IOptions<SpecGenInferenceConfig>`, uses config values at all 3 call sites (text call, vision call, timeout)
- [x] `ArtifactGenerationService.cs` — fixed to pass explicit `string modelId`
- [x] `dotnet build` — 0 errors, 0 warnings

---

### Known edge cases / things Clint should scrutinize

1. **`ArtifactGenerationService.cs` fallback model** — The artifact gen service reads model from `_config["FortressAI:ModelId"]` and now null-coalesces to `"us.anthropic.claude-sonnet-4-6"`. This is functionally equivalent to the previous `modelId ?? DefaultModelId` behavior, but the fallback value changed from `claude-sonnet-4-5-20250929-v1:0` to `claude-sonnet-4-6`. This is intentional — artifact gen was already using `FortressAI:ModelId` (pointing to `sonnet-4-6`) before this WI. No behavioral regression.

2. **`SpecGenerationService` constructor param order** — `IOptions<SpecGenInferenceConfig>` was added before the optional `IDiscoveryService?` param. DI resolves by type so order doesn't matter for injection, but anyone constructing this directly (tests, if any) would need to update.

3. **No `DiscoveryService.cs` changes** — It already passes `modelId: _inferenceConfig.ModelId` explicitly. Confirmed untouched.

---

### Required ECS task definition env vars (for Rhodey)

After deploy, the ECS task definition needs these environment variables added:

```
Bedrock__SpecGen__ModelId       = us.anthropic.claude-sonnet-4-5-20250929-v1:0
Bedrock__SpecGen__VisionModelId = us.anthropic.claude-sonnet-4-5-20250929-v1:0
```

ASP.NET Core convention: `__` (double underscore) maps to nested config keys in AWS env var format.

The remaining 3 keys (`MaxTokens`, `VisionMaxTokens`, `TimeoutSeconds`) have correct defaults in `SpecGenInferenceConfig.cs` and do not require env vars unless the values need to be overridden per environment.

---

### How to test locally

```bash
cd ~/projects/fip/nexus
dotnet build src/FortressNexus.Web/FortressNexus.Web.csproj
# Confirm 0 errors

# Verify the new config class is registered correctly at startup:
# Run the app locally and check startup logs — should not see any DI resolution errors
# for SpecGenerationService
```
