# Build Report — WI #1673 — Discovery silently fails on resume

**Investigator:** Tony Stark (software-engineer)
**Date:** 2026-04-08
**WI:** [#1673](https://dev.azure.com/FortressAM/FAIT/_workitems/edit/1673)

---

## Task Def Environment Variables (nexus-web:24)

Checked all 14 env vars + 1 secret in `containerDefinitions[0]`:

- `Nexus__DiscoveryKnowledgeBaseId` — **NOT PRESENT**
- No `Bedrock__*` overrides present either
- Key Vault URI is `https://placeholder.vault.azure.net/` → **Key Vault is intentionally disabled** (skipped in Program.cs when URI contains "placeholder")

Config hierarchy resolution: `appsettings.json` → ECS env vars → no Key Vault. Neither source had a valid KB ID.

---

## CloudWatch Logs (stream: `ecs/nexus-web/cd062e800a8e46f9b80ed31ff1bb6876`)

Relevant lines from Fred's discovery session (`2026-04-08 20:04`):

```
[20:04:40 INF] [DISCOVERY] Session 79ee6f22-7d08-4d64-b16f-bfc33f4c0393 created for submission 1
[20:04:40 ERR] [KB_RETRIEVE] DiscoveryKnowledgeBaseId is not configured — KB retrieval skipped
[20:04:40 INF] [BEDROCK] Invoking model us.anthropic.claude-3-5-sonnet-20241022-v2:0, maxTokens=2048
[20:04:41 ERR] [DISCOVERY_GEN] Bedrock call failed for session 79ee6f22-7d08-4d64-b16f-bfc33f4c0393
    Amazon.BedrockRuntime.Model.ValidationException: The provided model identifier is invalid.
    at FortressNexus.Web.Services.BedrockService.InvokeAsync(...BedrockService.cs:line 68)
    at FortressNexus.Web.Services.Discovery.DiscoveryService.GenerateQuestionsAsync(...DiscoveryService.cs:line 298)
```

---

## Root Cause Analysis

Two compounding failures:

### Failure 1 — KB Config Missing (config-only fix, Rhodey)
- `Nexus:DiscoveryKnowledgeBaseId` is `TODO_FORGE_KB_ID` in `appsettings.json` 
- Nothing in ECS task def overrides it
- `BedrockKnowledgeBaseService.RetrieveAsync()` logs ERR and returns empty context — silently degrades
- **This does not cause the toast error** — it just means discovery runs without KB context

### Failure 2 — Invalid Bedrock Model ID (code/config fix — FIXED ✅)
- `appsettings.json` had `Bedrock:Discovery:ModelId = "us.anthropic.claude-3-5-sonnet-20241022-v2:0"` 
- This is a stale cross-region inference profile ID — Bedrock now rejects it with `ValidationException: The provided model identifier is invalid`
- `GenerateQuestionsAsync()` throws, catch block fires, questions status = Failed
- UI shows: "Couldn't generate questions — continuing to spec generation"
- **This IS the direct cause of Fred's toast error**

---

## Fix Applied

### Code Change (Commit `60beea1`)
Updated `appsettings.json` — changed both stale model IDs to `us.anthropic.claude-sonnet-4-6`:

```diff
 "Bedrock": {
-    "DiscoveryModelId": "us.anthropic.claude-3-5-sonnet-20241022-v2:0",
+    "DiscoveryModelId": "us.anthropic.claude-sonnet-4-6",
     "Discovery": {
-        "ModelId": "us.anthropic.claude-3-5-sonnet-20241022-v2:0",
+        "ModelId": "us.anthropic.claude-sonnet-4-6",
         "MaxTokens": 2048,
         "Temperature": 0.3
     }
 }
```

`us.anthropic.claude-sonnet-4-6` is the same model ID used as `DefaultModelId` in `BedrockService.cs` and confirmed working across the app.

### Build Result
```
Build succeeded.
0 Error(s), 0 Warning(s)
```

---

## Config Prescription (Rhodey — ECS task def update)

Two env vars need to be added to the nexus-web ECS task definition:

| Name | Value | Priority |
|------|-------|----------|
| `Nexus__DiscoveryKnowledgeBaseId` | `WYSKBKWHPL` | High — KB context improves question quality |

Note: The model ID fix is a code change (committed above) and will deploy with the next build. The KB ID is purely a config item for Rhodey to add to the task def.

---

## Deploy Note

Commit `60beea1` on `main` — will deploy automatically on next CodeBuild trigger. After deploy, discovery question generation will work again. KB context will be absent until Rhodey adds the env var.
