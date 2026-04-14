# Review Report — NEXUS ADO #1807 — SpecGen IOptions Config Refactor

**Reviewer:** Hawkeye (Clint Barton)
**Commit:** `71aead9`
**ADO WI:** #1807 | **Cycle:** 1
**Date:** 2026-04-13
**Risk:** Medium → Elevated (see C3)

---

## Verdict: NEEDS-CHANGES

Three issues found. One is a runtime blocker — `ArtifactGenerationService` uses an invalid model ID that causes silent empty output on every work item generation call. The IOptions wiring and SpecGen model IDs are clean.

---

## Spec Compliance Check

No formal developer brief with §2/§7 for this WI. Reviewing against the task description directly.

**Files touched (matches expectation):**
- `Services/SpecGenInferenceConfig.cs` — ✅ created
- `Program.cs` — ✅ `Configure<SpecGenInferenceConfig>` added
- `appsettings.json` — ✅ `Bedrock:SpecGen` section added
- `Services/BedrockService.cs` — ✅ `DefaultModelId` removed, `modelId` required
- `Services/SpecGenerationService.cs` — ✅ `IOptions<SpecGenInferenceConfig>` injected
- `Services/ArtifactGenerationService.cs` — ✅ explicit modelId from config

No out-of-scope files changed. ✅

---

## Consistency Audit

**Cross-file model ID check:**
| Value | File | Matches? |
|-------|------|----------|
| `SpecGenInferenceConfig.cs` C# default `ModelId` | `"us.anthropic.claude-sonnet-4-5-20250929-v1:0"` | ✅ matches appsettings.json |
| `SpecGenInferenceConfig.cs` C# default `VisionModelId` | `"us.anthropic.claude-sonnet-4-5-20250929-v1:0"` | ✅ matches appsettings.json |
| `appsettings.json` `Bedrock:SpecGen:ModelId` | `"us.anthropic.claude-sonnet-4-5-20250929-v1:0"` | ✅ correct versioned ID |
| `appsettings.json` `Bedrock:SpecGen:VisionModelId` | `"us.anthropic.claude-sonnet-4-5-20250929-v1:0"` | ✅ correct versioned ID |
| `appsettings.json` `FortressAI:ModelId` | `"us.anthropic.claude-sonnet-4-6"` | ❌ invalid — missing version suffix |
| `ArtifactGenerationService.cs:41` fallback literal | `"us.anthropic.claude-sonnet-4-6"` | ❌ same invalid ID |

---

## Critical Issues [1]

### C1: Invalid Model ID in ArtifactGenerationService — Silent Empty Output [BLOCKER]

- **File:** `Services/ArtifactGenerationService.cs` (line ~41) + `appsettings.json` (line ~30)
- **Category:** Correctness / Configuration
- **Issue:** `resolvedModelId = _config["FortressAI:ModelId"] ?? "us.anthropic.claude-sonnet-4-6"` — both the config value AND the fallback literal are `"us.anthropic.claude-sonnet-4-6"`. This is NOT a valid AWS Bedrock cross-region inference ID. Valid format requires the date suffix and version discriminator: `us.anthropic.claude-sonnet-4-6-YYYYMMDD-v1:0` (or the sonnet-4-5 equivalent `us.anthropic.claude-sonnet-4-5-20250929-v1:0`).
- **Evidence:**
  ```csharp
  // ArtifactGenerationService.cs:41
  var resolvedModelId = _config["FortressAI:ModelId"] ?? "us.anthropic.claude-sonnet-4-6";

  // appsettings.json:30
  "FortressAI": {
    "ModelId": "us.anthropic.claude-sonnet-4-6"
  }
  ```
- **Impact:** Bedrock returns `ValidationException: The provided model identifier is invalid`. The exception is swallowed at line ~60 (`catch (Exception ex)` → logs and returns empty list). Every call to `GenerateWorkItemsAsync` silently produces zero work items. No user-visible error. Complete silent failure.
- **Fix:**
  ```diff
  // appsettings.json
  "FortressAI": {
  -  "ModelId": "us.anthropic.claude-sonnet-4-6"
  +  "ModelId": "us.anthropic.claude-sonnet-4-5-20250929-v1:0"
  }

  // ArtifactGenerationService.cs:41
  - var resolvedModelId = _config["FortressAI:ModelId"] ?? "us.anthropic.claude-sonnet-4-6";
  + var resolvedModelId = _config["FortressAI:ModelId"] ?? "us.anthropic.claude-sonnet-4-5-20250929-v1:0";
  ```
  **Note:** If Tony intends to use Claude 4.6 here, the correct versioned cross-region ID is needed (`us.anthropic.claude-sonnet-4-6-YYYYMMDD-v1:0`). Coordinate with the deployment config.

---

## Important Issues [2]

### I1: No Null/Whitespace Guard on BedrockService modelId Parameter

- **File:** `Services/BedrockService.cs` (lines ~38, ~120)
- **Category:** Correctness / Defensive Programming
- **Issue:** `DefaultModelId` const was removed. Both `InvokeAsync` and `InvokeWithImageAsync` accept `string modelId` with no validation. An empty string or null produces `var model = modelId` → `ModelId = ""` on the SDK request → `AmazonBedrockRuntimeException` from the service layer, not a clear `ArgumentException`. The log prints `[BEDROCK] Invoking model , maxTokens=...` (blank model) — confusing to diagnose.
- **Fix:**
  ```csharp
  // Add at the top of both InvokeAsync and InvokeWithImageAsync:
  if (string.IsNullOrWhiteSpace(modelId))
      throw new ArgumentException("modelId must not be null or empty.", nameof(modelId));
  ```

### I2: BedrockService Registered as Scoped — AWS SDK Anti-Pattern

- **File:** `Program.cs` (line ~137)
- **Category:** Reliability / Resource Management
- **Issue:** `builder.Services.AddScoped<BedrockService>()` creates a new `AmazonBedrockRuntimeClient` per Blazor Server circuit. Per MEMORY.md (documented anti-pattern) and AWS SDK guidance, Bedrock runtime clients are designed to be Singleton — they maintain an internal HTTP connection pool. Scoped registration destroys connection pooling and increases socket overhead per concurrent user.
- **Evidence:**
  ```csharp
  // Program.cs:137
  builder.Services.AddScoped<BedrockService>();

  // BedrockService.cs:22 (constructor)
  _client = new AmazonBedrockRuntimeClient(...);
  ```
- **Fix:**
  ```diff
  - builder.Services.AddScoped<BedrockService>();
  + builder.Services.AddSingleton<BedrockService>();
  ```
  `BedrockService` implements `IDisposable` — DI handles singleton disposal at app shutdown correctly.

---

## Nitpicks [0]

None.

---

## Checks That Passed ✅

| Check | Result |
|-------|--------|
| `IOptions<SpecGenInferenceConfig>` wired correctly (`.Value` in constructor) | ✅ PASS |
| `appsettings.json` `Bedrock:SpecGen:ModelId` = correct sonnet-4-5 versioned ID | ✅ PASS |
| `appsettings.json` `Bedrock:SpecGen:VisionModelId` = correct sonnet-4-5 versioned ID | ✅ PASS |
| C# property initializer defaults match appsettings.json | ✅ PASS |
| `TimeoutSeconds` used in vision retry loop (not hardcoded 120) | ✅ PASS |
| `Configure<SpecGenInferenceConfig>` registered before `builder.Build()` | ✅ PASS |
| `SpecGenerationService` (text call site) — passes `_specGenConfig.ModelId` (non-null) | ✅ PASS |
| `SpecGenerationService` (vision call site) — passes `_specGenConfig.VisionModelId` (non-null) | ✅ PASS |
| `ArtifactGenerationService` null-coalesce present (cannot produce null) | ✅ PASS |
| `DiscoveryService` passes `_inferenceConfig.ModelId` (non-null via IOptions.Value) | ✅ PASS |
| Scope of changes — no out-of-scope files modified | ✅ PASS |

---

## What to Fix (for Tony)

**1. [BLOCKER] Fix model IDs in `appsettings.json` and `ArtifactGenerationService.cs`**

In `appsettings.json` update `FortressAI:ModelId` from `"us.anthropic.claude-sonnet-4-6"` to the correct fully-versioned Bedrock cross-region inference ID. Same fix needed for `Bedrock:DiscoveryModelId` and `Bedrock:Discovery:ModelId` (observed to have same problem — outside this PR's scope but should be fixed now while touching appsettings).

In `ArtifactGenerationService.cs:41`, update the fallback literal to match.

If Claude 4.6 is intended here, supply the correct versioned ID (format: `us.anthropic.claude-sonnet-4-6-YYYYMMDD-v1:0`).

**2. [MEDIUM] Add null guard to `BedrockService.InvokeAsync` and `InvokeWithImageAsync`**

Add `if (string.IsNullOrWhiteSpace(modelId)) throw new ArgumentException(...)` at the top of both methods.

**3. [MEDIUM] Change `AddScoped<BedrockService>()` → `AddSingleton<BedrockService>()` in `Program.cs`**

This is an existing anti-pattern now being carried forward. Fix it here while the services are being touched.

---

## CC Review Notes

CC (Claude Code) performed the file-by-file analysis. CC findings confirmed by Hawkeye:
- C3 (invalid model ID) independently identified by CC as blocking
- C1 (no null guard) independently identified by CC
- A1 (Scoped BedrockService) independently identified by CC — matches MEMORY.md documented anti-pattern
- All PASS findings verified against actual file contents
- No false positives from CC on this review

---

---

# Review Report — NEXUS ADO #1807 — Cycle 2

**Reviewer:** Hawkeye (Clint Barton)
**Commit:** `8716afb`
**ADO WI:** #1807 | **Cycle:** 2
**Date:** 2026-04-13
**Risk:** Medium → Resolved

---

## Verdict: ✅ PASS

All three Cycle 1 issues resolved correctly. Zero regressions. Ready to merge.

---

## Checks

| # | Check | Result |
|---|-------|--------|
| C3 | `sonnet-4-6` elimination — grep sweep across all `.cs` and `.json` source | ✅ PASS |
| C1 | `ArgumentException` guard — first statement in both `InvokeAsync` and `InvokeWithImageAsync` | ✅ PASS |
| A1 | `AddSingleton<BedrockService>()` — no captive scoped dependencies | ✅ PASS |
| I4 | SpecGen model IDs in appsettings.json not regressed by sweep | ✅ PASS |
| S5 | Scope gate — exactly 4 source files modified, no out-of-scope changes | ✅ PASS |

---

## Detail

### CHECK 1: Sonnet-4-6 Elimination
`grep -r "sonnet-4-6" ~/projects/fip/nexus/src/ --include="*.cs" --include="*.json" --exclude-dir={bin,obj}` → **zero matches**.

All occurrences corrected:
- `appsettings.json` `FortressAI:ModelId` → `"us.anthropic.claude-sonnet-4-5-20250929-v1:0"`
- `appsettings.json` `Bedrock:DiscoveryModelId` → `"us.anthropic.claude-sonnet-4-5-20250929-v1:0"`
- `appsettings.json` `Bedrock:Discovery:ModelId` → `"us.anthropic.claude-sonnet-4-5-20250929-v1:0"`
- `ArtifactGenerationService.cs:41` fallback literal → `"us.anthropic.claude-sonnet-4-5-20250929-v1:0"`

### CHECK 2: ArgumentException Guard Placement
Both methods confirmed — guard is the absolute first statement, before any AWS SDK call, logging, or object construction:

```csharp
// InvokeAsync — first statement (line 38)
if (string.IsNullOrWhiteSpace(modelId))
    throw new ArgumentException("modelId must be provided — DefaultModelId has been removed.", nameof(modelId));

// InvokeWithImageAsync — first statement (line 103)
if (string.IsNullOrWhiteSpace(modelId))
    throw new ArgumentException("modelId must be provided — DefaultModelId has been removed.", nameof(modelId));
```

### CHECK 3: Singleton + Captive Dependency
`Program.cs:137` → `builder.Services.AddSingleton<BedrockService>()` ✅

BedrockService constructor dependencies:
| Dependency | Lifetime | Safe? |
|---|---|---|
| `ILogger<BedrockService>` | Singleton | ✅ |
| `AmazonBedrockRuntimeClient` | Self-constructed (`new`) | ✅ |

No scoped services. No captive dependency risk.

### CHECK 4: SpecGen IDs Unchanged
```json
"SpecGen": {
  "ModelId": "us.anthropic.claude-sonnet-4-5-20250929-v1:0",
  "VisionModelId": "us.anthropic.claude-sonnet-4-5-20250929-v1:0"
}
```
Both untouched by the sonnet-4-6 sweep. ✅

### CHECK 5: Scope
Commit 8716afb touched exactly:
- `Program.cs` ✅
- `Services/ArtifactGenerationService.cs` ✅
- `Services/BedrockService.cs` ✅
- `appsettings.json` ✅
- `pipeline/*.md` (docs, excluded from scope gate) ✅

---

## CC Review Notes

CC (Claude Code) performed adversarial file-by-file analysis against all 5 checks. All checks returned PASS. No false positives. No regressions found.
