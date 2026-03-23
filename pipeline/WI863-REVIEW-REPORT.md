# Review Report: WI#863 — FAIT Backend Developer KB Tier

**Reviewer:** Hawkeye (Clint Barton)
**Commit:** `721820a`
**Date:** 2026-03-20
**Cycle:** 1
**Verdict:** ✅ PASS

---

## Overview

Adds `KbTier.Developer = 3` enum value, wires a Developer Knowledge Base (FORGE-DevTeam-Shared) through `KnowledgeBaseService`, `KbDocumentService`, and `HavenChatController`. Five files modified, all in `fait/src/`. Clean additive implementation following established patterns.

---

## P1 Checks

| # | Check | Result | Notes |
|---|-------|--------|-------|
| 1 | `KbTier.Developer = 3` — no disruption to existing enum values | ✅ PASS | Additive append, Personal=0/Team=1/Corporate=2 unchanged |
| 2 | `DevKbId`/`DevDataSourceId` read from `IConfiguration`, not hardcoded | ✅ PASS | Both KnowledgeBaseService and KbDocumentService use config |
| 3 | `RetrieveDevAsync` follows `RetrieveCorpAsync` pattern, no auth filter | ✅ PASS | Identical structure, Score>0.3, NumberOfResults=3, shared KB by design |
| 4 | `HavenChatController` "dev" case calls `RetrieveDevAsync` (not wrong method) | ✅ PASS | Both Chat and KbSearch "dev" cases correctly call `RetrieveDevAsync` |
| 5 | `kb-list` endpoint includes Developer KB entry | ✅ PASS | Entry with `id="dev"`, `available` guard reads config |
| 6 | No files outside `fait/src/` modified | ✅ PASS | All 5 files in `fait/src/` only |
| 7 | `appsettings.json` change is in FAIT project (not FAM OS) | ✅ PASS | `fait/src/FortressAI.Web/appsettings.json` — correct |
| 8 | Empty placeholder values in appsettings (not hardcoded KB IDs) | ✅ PASS | `"DevKbId": ""` and `"DevDataSourceId": ""` |
| 9 | Frontend `"dev"` key matches backend `case "dev":` | ✅ PASS | See Frontend Consistency section |

---

## File-by-File Analysis

### 1. `FortressAI.Shared/Models/KbEntry.cs`
Enum append is correct. No disruption to existing values. Clean.

### 2. `FortressAI.Web/Services/KnowledgeBaseService.cs`
`_devKbId` injected via constructor from `IConfiguration["KnowledgeBase:DevKbId"]`.  
`RetrieveDevAsync` is a clean mirror of `RetrieveCorpAsync`:
- Same `NumberOfResults = 3`
- Same `Score > 0.3` filter
- Same graceful exception handling (returns empty list on failure)
- No auth/ownership filter — correct, dev KB is team-scoped via structural isolation
- `KbType = "Developer"` is a display label (Corp uses `"Fortress"`) — both semantically appropriate

### 3. `FortressAI.Web/Services/KbDocumentService.cs`
- `DevKbId` and `DevDataSourceId` properties are config-backed ✅
- S3 prefix `kb-docs/dev/` is flat (no user sub-path) — correct for shared KB ✅
- `UploadDocumentAsync` prefix switch covers Developer tier ✅
- `StartIngestionAsync` KB/DS switch covers all four non-default tiers consistently ✅
- `ListDocumentsAsync` prefix switch: clean refactor from if/else to switch expression, Developer handled ✅

### 4. `FortressAI.Web/Controllers/HavenChatController.cs`
- Chat endpoint `"dev"` case: calls `RetrieveDevAsync(request.Message)` — correct ✅
- KbSearch endpoint `"dev"` case: calls `RetrieveDevAsync(request.Query)` — correct ✅
- `kb-list` entry: `id="dev"`, `type="dev"`, `alwaysOn=false`, `available` checks config ✅

### 5. `fait/src/FortressAI.Web/appsettings.json`
Correct file (FAIT project). Empty string placeholders only. ✅

---

## Frontend Consistency Check

Commit `65936ba` was not found in this repo (likely separate frontend repo or branch). Reviewed current frontend code directly.

**Flow trace:**
1. `fetchKbList()` → `GET /api/haven/kb-list` → receives `{id: "dev", type: "dev", ...}`
2. `kbToggles` keyed by `kb.id` → `"dev"`
3. `buildKbTypes()` → `Object.entries(kbToggles).filter([,v] => v).map([k] => k)` → `["dev"]`
4. `sendChat()`/`sendChatStreaming()` sends `kbTypes: ["dev"]`
5. Backend: `kbType.ToLowerInvariant()` → `"dev"` → `case "dev":` ✅

**Note:** The task brief mentioned `tier: 'developer'` but the frontend uses `kb.id` as the kbTypes key — which is `"dev"`. The string `"developer"` does not appear in the frontend codebase. The `"dev"` key is consistent end-to-end.

---

## Findings

### Critical
*None.*

### Important
*None.*

### Nitpick

**N1 — KbSearch "dev" case missing success-level log**
The Chat endpoint logs `"[Haven] Dev KB returned {Count} chunks"` on success. The KbSearch "dev" case only logs on failure — no info-level log. Minor consistency gap with Chat.

**N2 — Pre-existing: `KbTier.Corporate` missing from `ListDocumentsAsync`**
*(NOT introduced by this commit — pre-existing since before this PR)*  
Before WI#863, the code was `tier == KbTier.Team ? teams/ : personal/` — Corporate already fell to personal. This commit refactored to a switch expression, making the gap more visible. `KbTier.Corporate` is not explicitly handled and falls to the `_` default (personal path). This is likely wrong behavior for Corporate doc listing. Recommend a follow-up WI.

---

## Verdict

**✅ PASS**

All 9 P1 checks green. Implementation is clean, additive, and pattern-consistent. No critical or important issues. Two nitpicks — one minor logging inconsistency, one pre-existing Corporate gap not introduced by this WI. Neither is a blocker.

**Recommended follow-up:** File WI for `KbTier.Corporate` in `ListDocumentsAsync` prefix logic.

---

*CC invocation: `cat /tmp/review-brief.md | claude --model sonnet -p`*
