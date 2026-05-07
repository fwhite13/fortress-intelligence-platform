# BUILD Brief: ADO#2859 — FAIT v2: Artifact generation - Word, Excel, PPT, HTML via CC

**ADO WI:** #2859 (Fortress project)
**Repo:** `/home/fredw/projects/fip`
**Service:** `fait-v2/src/FortressAI.V2.Web/`
**Sprint:** FAIT v2 Sprint 4

---

## MANDATORY: Use Claude Code CLI

```bash
CLAUDE_CODE_ENTRYPOINT=ado-pipeline \
CLAUDE_CODE_DISABLE_AUTO_MEMORY=1 \
CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1 \
CLAUDE_CODE_GLOB_TIMEOUT_SECONDS=30 \
cat /home/fredw/projects/fip/fait-v2/pipeline/ADO2859-BUILD-BRIEF.md | \
claude --model sonnet --print --dangerously-skip-permissions
```

Working directory: `/home/fredw/projects/fip/fait-v2/`

---

## Context

`ICCExecutionService` and `FargateCCExecutionService` are already implemented (#2857) — CC spawns as a child process and auto-uploads artifacts to S3. `IWorkspaceService` is also live (#2858). This WI wires artifact generation into the chat UI: a user requests a document, CC generates it, the artifact appears in the dual-pane preview or as a download link.

**Key existing infrastructure:**
- `Services/ICCExecutionService.cs` — `DispatchTaskAsync()` returns `CCExecutionResult` with `ArtifactS3Key` and `ArtifactType`
- `Services/IWorkspaceService.cs` — `GetDownloadUrlAsync()` for pre-signed S3 URLs
- `Components/Hubs/CCProgressHub.cs` — SignalR hub at `/hubs/cc-progress`
- `Components/Pages/Dashboard.razor` — main chat UI (needs artifact result display)
- Config: `AWS:WorkspaceBucket`, `CC:Model`

---

## Implementation

### 1. Add `ArtifactResult` model + Aurora metadata table

**Model** `Data/Models/ArtifactRecord.cs`:
```csharp
public class ArtifactRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;      // "docx", "xlsx", "pptx", "html", "json", "code"
    public string FileName { get; set; } = string.Empty;
    public string S3Key { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string? TaskDescription { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

Add `DbSet<ArtifactRecord> ArtifactRecords` to `FaitV2DbContext`. Add EF migration `AddArtifactRecords`.

### 2. Add `IArtifactService` + `ArtifactService`

`Services/IArtifactService.cs`:
```csharp
public interface IArtifactService
{
    Task<ArtifactRecord> RecordArtifactAsync(string userId, CCExecutionResult result, string taskDescription, CancellationToken ct = default);
    Task<string> GetDownloadUrlAsync(string userId, string artifactId, CancellationToken ct = default);
    Task<List<ArtifactRecord>> GetRecentArtifactsAsync(string userId, int limit = 10, CancellationToken ct = default);
}
```

`Services/ArtifactService.cs` — implementation:
- `RecordArtifactAsync`: after CC completes, record the artifact metadata in Aurora. Extract filename from S3Key. Use `IWorkspaceService` for the URL.
- `GetDownloadUrlAsync`: look up the ArtifactRecord, delegate to `IWorkspaceService.GetDownloadUrlAsync`
- `GetRecentArtifactsAsync`: query Aurora ordered by `CreatedAt DESC`

### 3. Wire artifact generation into `Dashboard.razor`

In the chat component, detect when the user's message requests artifact generation (keywords: "create", "generate", "write", "make" + file type hints like "word doc", "excel", "spreadsheet", "presentation", "report"). When detected, route to `ICCExecutionService.DispatchTaskAsync` instead of the standard Bedrock call.

**Progress display** — while CC runs, show an inline progress indicator in the chat:
```razor
@if (_ccRunning)
{
    <div class="chat-artifact-progress">
        <MudProgressLinear Indeterminate="true" Color="Color.Primary" />
        <span>@_ccCurrentStep</span>
        <MudButton Size="Size.Small" OnClick="CancelCCTask">Cancel</MudButton>
    </div>
}
```

Wire the `CCProgressHub` SignalR connection to update `_ccCurrentStep` in real time.

**Artifact result display** — when CC completes with an artifact:
```razor
@if (_lastArtifact != null)
{
    <div class="chat-artifact-result">
        <MudIcon Icon="@GetArtifactIcon(_lastArtifact.Type)" />
        <span>@_lastArtifact.FileName</span>
        @if (_lastArtifact.Type == "html")
        {
            <MudButton Size="Size.Small" OnClick="PreviewArtifact">Preview</MudButton>
        }
        <MudButton Size="Size.Small" OnClick="DownloadArtifact">Download</MudButton>
    </div>
}
```

For HTML artifacts, open in the dual-pane iframe. For others (docx/xlsx/pptx), show download button only.

### 4. `CCContextEnvelope` builder

When dispatching to CC, build the context envelope:
```csharp
private CCContextEnvelope BuildEnvelope(string userId, string displayName)
{
    return new CCContextEnvelope
    {
        UserId = userId,
        UserDisplayName = displayName,
        KbIds = new List<string>(), // populated from user's KB entitlements in a later WI
        EnabledMcpServers = new List<string>(), // populated from connector config in a later WI
        MemorySummary = null,       // memory integration in a later WI
        TaskInstructions = """
            You are an AI assistant generating a document artifact.
            Generate the requested document and write it to the working directory.
            Use python-docx for .docx, openpyxl for .xlsx, python-pptx for .pptx.
            For HTML, write a clean self-contained HTML file.
            The filename should be descriptive of the content.
            """
    };
}
```

### 5. Register in `Program.cs`

```csharp
builder.Services.AddScoped<IArtifactService, ArtifactService>();
```

### 6. CSS for artifact progress + result cards

Add to `wwwroot/css/app.css` using CSS variables only:
```css
.chat-artifact-progress { display: flex; align-items: center; gap: var(--spacing-sm); padding: var(--spacing-sm); background: var(--color-surface); border-radius: var(--border-radius-sm); border: 1px solid var(--color-border); }
.chat-artifact-result { display: flex; align-items: center; gap: var(--spacing-sm); padding: var(--spacing-sm); background: var(--color-surface-success, var(--color-surface)); border-radius: var(--border-radius-sm); border: 1px solid var(--color-border); }
```

---

## Constraints

- **CSS variables only** — no hardcoded colors/fonts/sizes
- **GuidFormat=MySqlGuidFormat.None** on all Aurora connections
- **varchar(36)** for GUID columns (`string` type in C#)
- **No Cognito**
- `dotnet build` 0 errors

---

## Acceptance Criteria

- [ ] `ArtifactRecord` model + EF migration `AddArtifactRecords`
- [ ] `IArtifactService` + `ArtifactService` implemented
- [ ] CC dispatch wired in `Dashboard.razor` for artifact requests
- [ ] Progress indicator shown while CC runs (with cancel button)
- [ ] Artifact result card shown on completion with download (+ preview for HTML)
- [ ] Artifact metadata recorded in Aurora after CC completes
- [ ] All services registered in `Program.cs`
- [ ] CSS via variables only
- [ ] `dotnet build` 0 errors

---

## ADO Tracking (MANDATORY)

After build complete:
```bash
mcporter call devops.add_comment --args '{
  "project": "Fortress",
  "id": 2859,
  "text": "**[Tony Stark — BUILD cycle 1]**\nCommit {hash}: {summary}. Build: SUCCEEDED."
}'
```

---

## Deliverables

1. `Data/Models/ArtifactRecord.cs` (new)
2. EF migration `AddArtifactRecords`
3. `Services/IArtifactService.cs` (new)
4. `Services/ArtifactService.cs` (new)
5. `Components/Pages/Dashboard.razor` (updated — CC dispatch + progress + artifact result)
6. `Program.cs` (updated — service registration)
7. `wwwroot/css/app.css` (updated — artifact styles)
8. Build Report: `/home/fredw/projects/fip/fait-v2/pipeline/ADO2859-BUILD-REPORT.md`
