# BUILD Plan — ADO#2809
## Seed NEXUS DB with FORGE KB MCP Server spec submission for end-to-end decomp test

**WI:** ADO#2809 | Feature #2805 | Epic #2793
**Repo:** `/home/fredw/projects/fip/nexus/`
**Risk:** low (config/seed only — no API changes, no new tables)

---

## Context

We need a real submission seeded in the NEXUS DB so Fred can walk through the full end-to-end flow:
**Review → Approve → Decompose → Tree Editor → Post to ADO**

The FORGE KB MCP Server spec is the designated test input (per the decomp upgrade acceptance test documented in memory). It is a real, full-length spec that will exercise all decomp logic.

**Spec file:** `/home/fredw/.openclaw/workspace/memory/projects/forge-kb-mcp-server-spec-2026-04-27.md`

---

## Implementation

### Add to `DatabaseInitializationService.cs`

After the existing NexusAdmin seed block, add an idempotent seed for the FORGE KB spec submission:

```csharp
// Seed FORGE KB MCP Server spec submission for E2E decomp test
const string forgeKbTitle = "FORGE KB MCP Server";
var hasForgeSubmission = await db.Submissions
    .AnyAsync(s => s.Title == forgeKbTitle && s.SubmittedBy == fredUpn);

if (!hasForgeSubmission)
{
    // Read spec content from embedded resource or inline — see below
    var specContent = /* read from file or inline constant */;

    var now = DateTime.UtcNow;

    // 1. Create submission
    var submission = new Submission
    {
        Title = forgeKbTitle,
        FeatureArea = "FORGE KB",
        NarrativeText = "FORGE KB MCP Server implementation spec — seeded for E2E decomp validation.",
        SubmittedBy = fredUpn,
        SubmittedAt = now,
        Status = SubmissionStatus.AwaitingReview,
        MockupFileId = null
    };
    db.Submissions.Add(submission);
    await db.SaveChangesAsync(); // get submission.Id

    // 2. Create SpecDocument with spec content
    var specDoc = new SpecDocument
    {
        SubmissionId = submission.Id,
        Version = 1,
        Content = specContent,
        GeneratedAt = now,
        GeneratedBy = "system-seed",
        IsApproved = false,
        PromptTokensUsed = 0,
        CompletionTokensUsed = 0
    };
    db.SpecDocuments.Add(specDoc);
    await db.SaveChangesAsync(); // get specDoc.Id

    // 3. Wire ActiveSpecDocumentId back
    submission.ActiveSpecDocumentId = specDoc.Id;
    await db.SaveChangesAsync();

    _logger.LogInformation("[NEXUS] Seeded FORGE KB spec submission id={Id} specDocId={SpecId}", 
        submission.Id, specDoc.Id);
}
```

### Reading the spec content

The spec file is at `/home/fredw/.openclaw/workspace/memory/projects/forge-kb-mcp-server-spec-2026-04-27.md` on the build host, but the ECS container won't have this path.

**Approach: embed the spec content as a C# string resource in the seed.**

Read the spec file content at build time and embed it as a verbatim string in `DatabaseInitializationService.cs`. This is a one-time seed — the content is static.

Steps:
1. Read the spec file content: `File.ReadAllText("/home/fredw/.openclaw/workspace/memory/projects/forge-kb-mcp-server-spec-2026-04-27.md")`
2. Escape it for embedding in C# — use a raw string literal (`"""..."""`) in C# 11+ (the project targets .NET 8, so raw string literals are available)
3. Store as `private const string ForgeKbSpecContent = """...""";` in the service, or better: as a separate file `Resources/forge-kb-spec-seed.md` embedded via `EmbeddedResource` in the `.csproj` and loaded with `Assembly.GetManifestResourceStream`.

**Recommended approach:** Embed as a `.md` file in `Resources/` and load via `Assembly.GetManifestResourceStream`. This keeps `DatabaseInitializationService.cs` readable.

```csharp
// In DatabaseInitializationService.cs:
var assembly = Assembly.GetExecutingAssembly();
using var stream = assembly.GetManifestResourceStream(
    "FortressNexus.Web.Resources.forge-kb-spec-seed.md");
using var reader = new StreamReader(stream!);
var specContent = await reader.ReadToEndAsync();
```

In `.csproj`:
```xml
<ItemGroup>
  <EmbeddedResource Include="Resources/forge-kb-spec-seed.md" />
</ItemGroup>
```

Copy the spec file to `src/FortressNexus.Web/Resources/forge-kb-spec-seed.md`.

**Alternative if embedded resource is complex:** Use a C# raw string literal in a separate static class `ForgeKbSpecSeed.Content`. Either approach is fine — choose whichever compiles cleanest.

---

## Required namespaces (add if missing)

```csharp
using System.Reflection;
using FortressNexus.Web.Models.Entities;
using FortressNexus.Web.Models.Enums;
```

---

## Acceptance Criteria

- [ ] On startup, if no submission with title "FORGE KB MCP Server" and `SubmittedBy = fwhite@...` exists, it is created
- [ ] Submission status = `AwaitingReview`
- [ ] `spec_documents` row created with full FORGE KB spec content in `content` column
- [ ] `submission.ActiveSpecDocumentId` points to the new spec doc
- [ ] Idempotent — second startup does NOT create a duplicate
- [ ] CloudWatch logs: `[NEXUS] Seeded FORGE KB spec submission id=X specDocId=Y`
- [ ] Build compiles with 0 errors
- [ ] Submission visible on NEXUS Dashboard for Fred

---

## Files to change

- `src/FortressNexus.Web/Services/DatabaseInitializationService.cs` — add seed block
- `src/FortressNexus.Web/Resources/forge-kb-spec-seed.md` — copy of spec content (new)
- `src/FortressNexus.Web/FortressNexus.Web.csproj` — add EmbeddedResource entry

---

## CC env vars
```bash
export CLAUDE_CODE_ENTRYPOINT=ado-pipeline
export CLAUDE_CODE_DISABLE_AUTO_MEMORY=1
export CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1
export CLAUDE_CODE_GLOB_TIMEOUT_SECONDS=30
```
