# Build Report — ADO #1960
**Agent:** Tony Stark (BUILD)
**Date:** 2026-04-15
**Commit:** `73a15a2`

## Changes

### `nexus/src/FortressNexus.Web/Services/BedrockService.cs`
- Added `["anthropic_beta"] = new JsonArray { "output-128k-2025-02-19" }` to `InvokeAsync` `requestObj`, immediately after `anthropic_version`
- `InvokeWithImageAsync` not modified (already has the header)

### `nexus/src/FortressNexus.Web/appsettings.json`
- `Bedrock.SpecGen.MaxTokens`: `8192` → `32768`
- `VisionMaxTokens` (2000) and all other values unchanged

## Build Result
```
1 Warning(s)
0 Error(s)
Time Elapsed 00:00:04.80
```
**Status: SUCCEEDED**

## Acceptance Criteria
1. ✅ `InvokeAsync` `requestObj` has `anthropic_beta` after `anthropic_version`
2. ✅ `InvokeWithImageAsync` not modified
3. ✅ `appsettings.json` `SpecGen.MaxTokens` = `32768`
4. ✅ `dotnet build` → 0 errors
5. ⏳ DB reset — pending Rhodey deploy + Tony DB reset
