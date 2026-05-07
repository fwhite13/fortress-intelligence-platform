# ADO#2909 — Review Report

**Reviewer:** Hawkeye (code-reviewer)
**Cycle:** REVIEW cycle 1
**Date:** 2026-05-07
**Commit:** `47cffba` fix(ADO#2909): §15 OQ resolutions + plugin/connector terminology corrections
**Verdict:** PASS

---

## Files Reviewed

| File | Status |
|------|--------|
| `Services/IPluginAgentService.cs` | PASS |
| `Services/IRAGWriteService.cs` | PASS |
| `Services/IRAGReadService.cs` | PASS |
| `Services/IMemoryFileService.cs` | PASS |
| `Components/Agent/AssistantLoadingState.razor` | PASS |

## Findings

### Critical
None.

### Important
None.

### Nitpick
1. **Unused using in IMemoryFileService.cs** — `using System.IO.Compression` (line 1) is unused in this interface-only file. No `ZipArchive` or compression types are referenced. Harmless but unnecessary — can be cleaned up when the implementation is added in Sprint 3.

## Review Details

### OQ-15-3: DiscoveredFact record
- `DiscoveredFact(string Fact, string Source)` — positional record, no `Confidence` property. Correct per spec.
- XML doc clearly states quality gate is in prompt engineering, not post-hoc filtering.
- Placement alongside `IPluginAgentService` and `McpServerPermission` is logical.

### OQ-15-1: IRAGWriteService stub
- Three-method interface correctly separates extraction entry (`QueueExtractionAsync`), synchronous pgvector upsert (`WriteFactAsync`), and async background merge (`MergeToMemoryFileAsync`).
- `MemoryChunk` record well-structured: `UserId`, `TopicSlug`, `Content`, `Source`, `CreatedAt`.
- All methods async with `CancellationToken` support. Clean.

### OQ-15-2: IRAGReadService stub + warmup TODO
- `WarmupAsync(string userId, CancellationToken)` — correct cold-start hook signature.
- `RetrieveAsync` returns `IReadOnlyList<MemoryChunk>` — correct cross-reference to `MemoryChunk` in same namespace.
- TODO comment in `AssistantLoadingState.razor` (line 60) correctly placed at the poll entry point with Sprint 3 and OQ-15-2 attribution.

### OQ-15-5: Memory export .md-only constraint
- Comment `// OQ-15-5: export returns .md files only, not pgvector chunks` placed correctly above `ExportZipAsync` in `IMemoryFileService.cs`.

### Terminology audit
- Grep scan for incorrect connector/plugin terminology (`MS365 plugin`, `ADO plugin agent`, etc.) returned zero matches.
- Connectors (`ConnectorService`/`IConnectorService`) and plugin agents (`AgentPlugin`/`IPluginAgentService`) are correctly separated. No corrections needed.

### CSS-variable rule
- Not applicable — no UI styling changes in this commit.

## CC Invocation
```
claude --permission-mode bypassPermissions --print
```

---

**Result:** PASS — all OQ resolutions implemented correctly. One cosmetic nitpick (unused using) deferred to Sprint 3.
