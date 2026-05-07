# ADO#2909 — Build Report

**Agent:** Tony Stark (software-engineer)
**Cycle:** BUILD cycle 1
**Date:** 2026-05-07
**Commit:** `47cffba` fix(ADO#2909): §15 OQ resolutions + plugin/connector terminology corrections
**Build:** SUCCEEDED (0 errors, 0 warnings)

---

## Changes Implemented

### 1. DiscoveredFact record — OQ-15-3 (no confidence scoring)
- **File:** `src/FortressAI.V2.Web/Services/IPluginAgentService.cs`
- Added `DiscoveredFact(string Fact, string Source)` record
- No confidence property — all facts from plugin agents are persisted; quality gate is in prompt engineering

### 2. IRAGWriteService stub — OQ-15-1 (sync writes + async merges)
- **File:** `src/FortressAI.V2.Web/Services/IRAGWriteService.cs` (new)
- `QueueExtractionAsync()` — extraction pipeline entry point
- `WriteFactAsync(MemoryChunk)` — synchronous pgvector upsert (available next turn)
- `MergeToMemoryFileAsync()` — async background queue for topic .md merges
- `MemoryChunk` record defined in same file

### 3. IRAGReadService stub — OQ-15-2 (cold-start warmup)
- **File:** `src/FortressAI.V2.Web/Services/IRAGReadService.cs` (new)
- `WarmupAsync()` — pgvector no-op query on cold start
- `RetrieveAsync()` — similarity search for memory retrieval

### 4. Cold-start pgvector warmup TODO — OQ-15-2
- **File:** `src/FortressAI.V2.Web/Components/Agent/AssistantLoadingState.razor`
- Added `// TODO Sprint 3:` comment at the cold-start poll entry point to wire `IRAGReadService.WarmupAsync()`

### 5. Memory export .md-only constraint — OQ-15-5
- **File:** `src/FortressAI.V2.Web/Services/IMemoryFileService.cs`
- Added `// OQ-15-5: export returns .md files only, not pgvector chunks` comment on export section

### 6. Plugin/connector terminology audit
- Scanned all `src/` files for incorrect usage of "MS365 plugin", "ADO plugin agent", etc.
- **Result:** terminology is already correct — connectors (MS365, ADO, FORGE KB, Web Search) are in `ConnectorService`/`IConnectorService`; plugin agents (Marketing, Finance, Legal) are in `AgentPlugin`/`IPluginAgentService`
- No corrections needed

## Files Changed
| File | Action |
|------|--------|
| `Services/IPluginAgentService.cs` | Added `DiscoveredFact` record |
| `Services/IRAGWriteService.cs` | New — interface stub + `MemoryChunk` record |
| `Services/IRAGReadService.cs` | New — interface stub with `WarmupAsync` |
| `Services/IMemoryFileService.cs` | Added OQ-15-5 export comment |
| `Components/Agent/AssistantLoadingState.razor` | Added warmup TODO comment |

## Not In Scope (deferred to Sprint 3)
- CompactionService implementation
- RAGWriteService / RAGReadService concrete implementations
- pgvector schema + EF integration
- DI registration for RAG services
