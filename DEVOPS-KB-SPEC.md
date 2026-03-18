# DevOps KB Spec — Large-Chunk, Code-Optimized Bedrock KB for CC Sessions

**Author:** Reed Richards (Software Architect)  
**Date:** 2026-03-17  
**Status:** Ready for Implementation  
**Target:** Tony Stark (software-engineer) via CC + Rhodey (infrastructure provisioning)  
**Reviewer:** Clint Barton (code-reviewer)  
**Users:** Rob, Len, Leslie (CC on Ubuntu VMs); FAIT users who manage the KB

---

## Pre-Read: What Was Confirmed

**Existing KB infrastructure in FAIT:**
- `KbTier` enum: `Personal = 0`, `Team = 1`, `Corporate = 2` — three values only
- `KbDocumentService.UploadDocumentAsync(stream, filename, contentType, KbTier, userId, teamId?)`
- S3 prefixes: `kb-docs/personal/<userId>/`, `kb-docs/teams/<teamId>/`, `kb-docs/fortress/`
- `KnowledgeBaseService` has 4 Bedrock KB IDs: `CorpKbId`, `PersonalKbId`, `TeamKbId`, `ProjectKbId`
- `HavenChatController` KB type routing: `"corp"`, `"personal"`, `"team"` switch cases
- All existing KBs use Bedrock's default chunk strategy (fixed-size, ~300 tokens, ~10% overlap) — optimized for prose documents and Q&A retrieval

**CC Memory MCP server** (`CC-MEMORY-SPEC.md`) is a separate, pgvector-backed system for cross-session memory (decisions, lessons, short entries). The DevOps KB is complementary — it holds larger technical artifacts (architecture docs, runbooks, code comments, ADRs) that are too large for the memory store and require code-optimized chunking.

---

## Architecture Decision: Separate Bedrock KB vs. Same KB with Different Chunking

**Option A — Same Bedrock KB, different S3 prefix:** Put dev docs in `kb-docs/dev/`. Use the existing Corp KB. No new Bedrock resource.

**Option B — New Bedrock KB with code-optimized chunking:** New KB with a 3000-token fixed chunk size and 20% overlap. Code files stay coherent across chunk boundaries — function bodies don't get split.

**Decision: Option B.** Rationale:
- The existing Corp KB uses ~300-token chunks optimized for prose Q&A. Code files (300-3000 lines) need much larger chunks to be retrievable as coherent units.
- A 300-token chunk of a TypeScript file contains about 50 lines — typically one function or less. A developer asking "how does the FAIT auth flow work" would need 10+ chunks to reconstruct a file. With 3000-token chunks, one chunk covers 200–400 lines.
- Bedrock does not support per-document chunk size overrides within a single data source. A separate KB is required for different chunk config.
- Cost: one additional Bedrock KB = ~$0.10/GB/month storage + ~$0.0004/query. For typical CC usage (20-30 queries/day), this is < $5/month.

---

## Bedrock KB Configuration (Rhodey — Infrastructure)

### New Bedrock KB: "FIP DevOps"

```
KB Name: fip-devops-kb
Embedding model: amazon.titan-embed-text-v2:0 (1536-dim, same as other KBs)
Storage: existing OpenSearch Serverless collection (or create new if quota hit)
```

### Data Source Configuration

```
Data source name: fip-devops-s3
Type: Amazon S3
Bucket: fortress-tools (existing — same bucket as other KB docs)
Inclusion prefix: kb-docs/dev/
```

### Chunking Strategy

```
Strategy: Fixed size
Max tokens per chunk: 3000
Overlap: 600 tokens (20%)
```

Why 3000 tokens:
- Average TypeScript/C# function: 20–80 lines ≈ 200–800 tokens
- A 3000-token chunk covers ~300 lines — enough for a complete class or service
- Titan Embed v2 supports up to 8192 tokens — 3000 is well within the limit
- Bedrock Retrieve returns up to 5 chunks per query by default — 5 × 3000 = 15,000 tokens of context, sufficient for CC

### Metadata Filtering

Each document in `kb-docs/dev/` gets a `.metadata.json` companion (same pattern as personal KB):

```json
{
  "metadataAttributes": {
    "repo": "fip",           // "fip", "firm", "forms", etc.
    "fileType": "typescript", // "typescript", "csharp", "markdown", "yaml"
    "uploadedBy": "<email>"
  }
}
```

CC can filter by repo at query time: `{ filter: { equals: { key: "repo", value: "fip" } } }`.

In Sprint 1, no filtering UI needed — store metadata but query without filters. Sprint 2 can add filter toggles.

---

## S3 Prefix Structure

```
fortress-tools/
└── kb-docs/
    ├── dev/                     ← DevOps KB docs (new)
    │   ├── fip-program-cs.md    ← Converted from source file
    │   ├── fip-program-cs.md.metadata.json
    │   ├── firm-architecture.md
    │   ├── firm-architecture.md.metadata.json
    │   ├── ecs-task-defs.md
    │   └── ecs-task-defs.md.metadata.json
    ├── fortress/                ← Corp KB (unchanged)
    ├── personal/                ← Personal KB (unchanged)
    └── teams/                   ← Team KB (unchanged)
```

**File format policy:** All docs in `kb-docs/dev/` are `.md` (Markdown). Why:
- Bedrock KB supports `.md`, `.txt`, `.pdf`, `.docx`, `.csv`
- Source code uploaded as-is (`.ts`, `.cs`) is not supported natively — must be wrapped in a markdown code block
- Markdown preserves structure (headings, code blocks) and gives Bedrock chunk boundaries to work with
- FAIT's upload pipeline already supports `.md` — no conversion needed

**Conversion convention for source files:**
```markdown
# src/FortressAI.Web/Program.cs

**Repo:** fip  
**Type:** C#  
**Last updated:** 2026-03-17

\```csharp
// (full file content here)
\```
```

Uploaders (Rob, Len, Leslie) wrap source files in this template before uploading. Later: a GitHub Action or CLI tool automates this.

---

## FAIT Backend Changes

### 1. Add `Developer = 3` to `KbTier` Enum

**File:** `fip/fait/src/FortressAI.Shared/Models/KbEntry.cs`

```csharp
public enum KbTier { Personal = 0, Team = 1, Corporate = 2, Developer = 3 }
```

### 2. `KbDocumentService.cs` — Add Dev KB Support

**Add config properties** (same pattern as existing):

```csharp
private string DevKbId => _config["KnowledgeBase:DevKbId"] ?? "";
private string DevDataSourceId => _config["KnowledgeBase:DevDataSourceId"] ?? "";
```

**Update S3 prefix switch** in `UploadDocumentAsync`:

```csharp
var s3Key = tier switch
{
    KbTier.Team      => $"kb-docs/teams/{teamId}/{safeFilename}",
    KbTier.Corporate => $"kb-docs/fortress/{safeFilename}",
    KbTier.Developer => $"kb-docs/dev/{safeFilename}",      // <-- new
    _                => $"kb-docs/personal/{userId}/{safeFilename}"
};
```

**Metadata companion for dev docs** (no user-scoped metadata — dev KB is shared):

```csharp
if (tier == KbTier.Developer)
{
    var metadata = new
    {
        metadataAttributes = new Dictionary<string, object>
        {
            ["uploadedBy"] = userId.ToString()
        }
    };
    // Upload .metadata.json companion (same pattern as personal KB)
    ...
}
```

**Update `StartIngestionAsync` switch:**

```csharp
var (kbId, dsId) = tier switch
{
    KbTier.Personal  => (PersonalKbId, PersonalDataSourceId),
    KbTier.Team      => (TeamKbId, TeamDataSourceId),
    KbTier.Corporate => (CorpKbId, CorpDataSourceId),
    KbTier.Developer => (DevKbId, DevDataSourceId),        // <-- new
    _                => throw new ArgumentOutOfRangeException(nameof(tier))
};
```

### 3. `KnowledgeBaseService.cs` — Add `RetrieveDevAsync()`

```csharp
private readonly string _devKbId;

// In constructor:
_devKbId = config["KnowledgeBase:DevKbId"] ?? "";

public async Task<List<KbChunk>> RetrieveDevAsync(string query)
{
    if (string.IsNullOrEmpty(_devKbId)) return new();
    return await RetrieveAsync(query, _devKbId, numberOfResults: 5);
}
```

### 4. `HavenChatController.cs` — Add `"dev"` KB Type

In both `Chat` and `KbSearch` action methods, add a new `case "dev":` to the KB type switch:

```csharp
case "dev":
    try
    {
        var chunks = await _kbService.RetrieveDevAsync(request.Message);
        allChunks.AddRange(chunks);
        _logger.LogInformation("[Haven] Dev KB returned {Count} chunks", chunks.Count);
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "[Haven] Dev KB retrieval failed");
    }
    break;
```

### 5. FAIT Admin UI — "Dev KB" Upload Section

**Where:** FAIT's admin KB management page (wherever Corp KB upload currently lives — check `AdminIndex.razor` or similar).

The Dev KB upload section should match the Corp KB upload UX but with:
- Visible label: **"Dev KB"** with subtitle: "Code docs, runbooks, architecture — large-chunk retrieval for CC sessions"
- Any authenticated FAIT user can upload (not admin-only)
- File types accepted: `.md`, `.txt`, `.pdf` (`.ts`, `.cs` rejected with guidance: "Wrap code files in Markdown first")
- Show list of existing `kb-docs/dev/` files (via `KbDocumentService.ListDocumentsAsync(KbTier.Developer, ...)`)
- Delete button per document (same pattern as personal KB delete)

This is a Blazor UI change to the existing KB management page. Exact file: check `AdminIndex.razor` or wherever `KbTier.Corporate` uploads are handled today — add a parallel section for `KbTier.Developer`.

---

## CC Memory MCP Integration

The DevOps KB and the CC Memory MCP server are **separate query paths**. They serve different purposes:

| | DevOps KB | CC Memory |
|--|-----------|-----------|
| Backend | Bedrock KB (vector store: OpenSearch) | pgvector |
| Chunk size | 3000 tokens | Short entries (1–3 sentences) |
| Content type | Full docs, code files, runbooks | Decisions, lessons, session notes |
| Query path | Via FAIT `/api/haven/kb-search` with `kbTypes: ["dev"]` | Via MCP `memory_search` tool |
| Who writes | Any FAIT user via admin UI | CC via `memory_add`; CLI; git hook |
| Latency | 200–500ms (Bedrock Retrieve) | 10–50ms (pgvector) |

**CC `CLAUDE.md` template addition** (extends the template from `CC-MEMORY-SPEC.md`):

```markdown
## Dev KB
For code and architecture context: use FAIT KB search (not memory_search).
At session start: search both memory AND Dev KB for project context.
Dev KB query: POST https://fait.dev.fortressam.ai/api/haven/kb-search
  Body: {"query": "...", "kbTypes": ["dev"], "topK": 5}
  Header: x-api-key: <AppKeys:ExcelAddin or Entra Bearer>
```

**Why not route Dev KB through the MCP memory server?** The MCP memory server uses pgvector with small chunks and a 1536-dim embedding optimized for short decisions. 3000-token code chunks would overwhelm the cosine similarity scores and perform poorly. Bedrock Retrieve is purpose-built for large document retrieval. Two separate paths is the correct architecture.

---

## Access Control

**Who can write to Dev KB:**
- Any authenticated FAIT user via the admin UI Dev KB upload section
- No admin-only gate — Rob, Len, Leslie, and Elise can all upload
- Rationale: it's a shared knowledge resource; bad uploads can be deleted

**Who can read Dev KB:**
- `HavenChatController` returns Dev KB chunks when `kbTypes: ["dev"]` is passed
- The Haven endpoint is auth-gated (`ExcelAddinAccess` policy)
- CC sessions use the ExcelAddin AppKey or Entra Bearer — both work
- No per-user scoping on Dev KB reads (it's org-level shared)

**Audit:** Every upload/delete is logged (`_logger.LogInformation`) with userId + filename. No separate audit table needed for Sprint 1.

---

## New Environment Variables (FAIT ECS Task Definition)

```
KnowledgeBase__DevKbId=<Bedrock KB ID from Rhodey>
KnowledgeBase__DevDataSourceId=<Bedrock Data Source ID from Rhodey>
```

---

## Files Changed Summary

### Modified Files (FAIT)

| File | Change |
|------|--------|
| `FortressAI.Shared/Models/KbEntry.cs` | Add `Developer = 3` to `KbTier` enum |
| `FortressAI.Web/Services/KbDocumentService.cs` | Add `DevKbId`/`DevDataSourceId` config; add `kb-docs/dev/` prefix; update `StartIngestionAsync` |
| `FortressAI.Web/Services/KnowledgeBaseService.cs` | Add `_devKbId`; add `RetrieveDevAsync()` |
| `FortressAI.Web/Controllers/HavenChatController.cs` | Add `case "dev":` to both KB type switches |
| Admin KB UI razor page (TBD exact filename) | Add "Dev KB" upload/list/delete section |

**Infrastructure (Rhodey):**
- Create new Bedrock KB `fip-devops-kb` with 3000-token fixed-size chunking
- Create data source pointing to `fortress-tools/kb-docs/dev/`
- Add `KnowledgeBase__DevKbId` + `KnowledgeBase__DevDataSourceId` to FAIT ECS task def

**Total: 5 modified files (FAIT). No new npm packages. No schema changes.**

---

## Acceptance Criteria

1. **Upload:** Rob uploads `firm-architecture.md` via the FAIT admin Dev KB section. File appears in `s3://fortress-tools/kb-docs/dev/firm-architecture.md`. Ingestion job starts.

2. **Query via FAIT:** `POST /api/haven/kb-search` with `{ "query": "FIRM bot join architecture", "kbTypes": ["dev"], "topK": 5 }`. Returns chunks from the uploaded architecture doc. Chunk content is ~300 lines of Markdown (3000-token chunks).

3. **CC session:** Rob's `CLAUDE.md` instructs CC to query Dev KB. CC calls `/api/haven/kb-search` with `kbTypes: ["dev"]`. Returns relevant code context. CC uses it to answer "how does the FIRM meeting join endpoint work?"

4. **User access:** Leslie (not an admin) can upload to Dev KB. The upload succeeds with a 200 response.

5. **Dev KB isolated from Corp KB:** `POST /api/haven/kb-search` with `kbTypes: ["corp"]` does NOT return Dev KB content. `kbTypes: ["dev"]` does NOT return Corp KB content.

6. **Delete:** Rob deletes `firm-architecture.md` from the Dev KB section. It's removed from S3 and re-ingestion is triggered.

---

## Clint Review Priorities

```
⚠️  HIGH: Verify KbTier.Developer = 3 doesn't break existing switch statements
          that handle the enum. Every switch/match on KbTier must have a case
          for Developer or a default clause. The most critical is KbDocumentService
          StartIngestionAsync — a missing case throws at runtime when a dev upload
          triggers ingestion.

⚠️  HIGH: Verify the Dev KB upload in the FAIT admin UI does NOT require admin
          role. KbTier.Corporate uploads are admin-only. KbTier.Developer must
          be available to all authenticated users. Check that the auth check
          on the Dev KB upload form is based on IsAuthenticated, not IsAdmin.

⚠️  MEDIUM: Verify HavenChatController "dev" case uses the same try/catch
            pattern as the "corp" and "team" cases. A Dev KB retrieval failure
            must be non-fatal — the response should still return content from
            other requested KB types.

⚠️  MEDIUM: Verify the 3000-token chunk config is set on the DATA SOURCE,
            not the KB level. In Bedrock, chunking strategy is configured on
            the data source's chunking configuration, not on the KB itself.
            Rhodey must set this in the Bedrock console, not in code.

⚠️  LOW: Verify .metadata.json companion files use the correct S3 naming
         convention: <filename>.metadata.json (with the full filename including
         extension). E.g. firm-architecture.md.metadata.json, NOT
         firm-architecture.metadata.json. Bedrock expects the double extension.
```

---

_Spec by Reed Richards | DevOps KB: 5 modified FAIT files + Rhodey infrastructure task. Separate Bedrock KB with 3000-token chunks. Dev KB is CC-queryable via `kbTypes: ["dev"]`. Separate from pgvector CC Memory MCP (different content type, different query path)._
