# BUILD Brief: ADO#2857 — FAIT v2: CC child process orchestration within Fargate task

**ADO WI:** #2857 (Fortress project)
**Repo:** `/home/fredw/projects/fip`
**Service:** `fait-v2/src/FortressAI.V2.Web/`
**Sprint:** FAIT v2 Sprint 4 — CC Artifact Spawning + FAIT v1 Continuity

---

## MANDATORY: Use Claude Code CLI

```bash
CLAUDE_CODE_ENTRYPOINT=ado-pipeline \
CLAUDE_CODE_DISABLE_AUTO_MEMORY=1 \
CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1 \
CLAUDE_CODE_GLOB_TIMEOUT_SECONDS=30 \
cat /home/fredw/projects/fip/fait-v2/pipeline/ADO2857-BUILD-BRIEF.md | \
claude --model sonnet --print --dangerously-skip-permissions
```

Working directory: `/home/fredw/projects/fip/fait-v2/`

---

## Context

FAIT v2 is an ASP.NET 8 Blazor Server app at `src/FortressAI.V2.Web/`. It currently has `IUserAgentRuntime` / `FargateUserAgentRuntime` stubs and a `CCProgressHub` SignalR hub stub. This WI implements the CC child process execution service.

**Current state (from source inspection):**
- `Services/IUserAgentRuntime.cs` — exists (Fargate session management interface)
- `Services/FargateUserAgentRuntime.cs` — exists (ECS task management)
- No `ICCExecutionService` exists yet

**Key architectural decision:** CC runs as a child process within the FAIT v2 Fargate container (not a separate platform). The Blazor app dispatches a task, CC runs inline, progress streams via SignalR, output artifact saved to S3.

---

## Implementation

### 1. Define `ICCExecutionService` interface
File: `Services/ICCExecutionService.cs`

```csharp
public interface ICCExecutionService
{
    Task<CCExecutionResult> DispatchTaskAsync(
        string userId,
        string task,
        CCContextEnvelope contextEnvelope,
        IProgress<CCProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default);
}

public class CCContextEnvelope
{
    public string UserId { get; set; } = string.Empty;
    public string UserDisplayName { get; set; } = string.Empty;
    public List<string> KbIds { get; set; } = new();
    public List<string> EnabledMcpServers { get; set; } = new();
    public string? MemorySummary { get; set; }
    public string TaskInstructions { get; set; } = string.Empty;
}

public class CCExecutionResult
{
    public bool Success { get; set; }
    public string? ArtifactS3Key { get; set; }
    public string? ArtifactType { get; set; }  // "html", "docx", "xlsx", "pptx", "json", "code"
    public string? Output { get; set; }  // last CC stdout text
    public string? Error { get; set; }
    public TimeSpan Duration { get; set; }
}

public class CCProgressUpdate
{
    public string TaskId { get; set; } = string.Empty;
    public string CurrentStep { get; set; } = string.Empty;
    public List<string> CompletedSteps { get; set; } = new();
    public List<string> ToolCallsMade { get; set; } = new();
    public TimeSpan ElapsedTime { get; set; }
    public string? InterventionPrompt { get; set; }
}
```

### 2. Implement `FargateCCExecutionService`
File: `Services/FargateCCExecutionService.cs`

Implements `ICCExecutionService`. Spawns CC as a child process:

```csharp
public async Task<CCExecutionResult> DispatchTaskAsync(
    string userId,
    string task,
    CCContextEnvelope contextEnvelope,
    IProgress<CCProgressUpdate>? progress = null,
    CancellationToken cancellationToken = default)
{
    var taskId = Guid.NewGuid().ToString("N")[..8];
    var startTime = DateTime.UtcNow;
    
    // Build the prompt text to pipe to CC
    var prompt = BuildPrompt(contextEnvelope, task);
    
    // CC command: claude --model sonnet --print --dangerously-skip-permissions
    var psi = new ProcessStartInfo
    {
        FileName = "claude",
        Arguments = "--model sonnet --print --dangerously-skip-permissions",
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        WorkingDirectory = GetUserWorkDir(userId),
    };
    
    // Set CC env vars
    psi.Environment["CLAUDE_CODE_ENTRYPOINT"] = "ado-pipeline";
    psi.Environment["CLAUDE_CODE_DISABLE_AUTO_MEMORY"] = "1";
    psi.Environment["CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR"] = "1";
    psi.Environment["CLAUDE_CODE_GLOB_TIMEOUT_SECONDS"] = "30";
    
    using var process = new Process { StartInfo = psi };
    var outputLines = new List<string>();
    var completedSteps = new List<string>();
    var toolCalls = new List<string>();
    
    process.OutputDataReceived += (sender, e) =>
    {
        if (e.Data == null) return;
        outputLines.Add(e.Data);
        
        // Parse CC progress from output
        // Lines starting with "Tool call:" or similar patterns
        if (e.Data.StartsWith("Tool:") || e.Data.Contains("Called "))
            toolCalls.Add(e.Data);
        
        // Report progress
        progress?.Report(new CCProgressUpdate
        {
            TaskId = taskId,
            CurrentStep = e.Data.Length > 100 ? e.Data[..100] + "..." : e.Data,
            CompletedSteps = completedSteps.ToList(),
            ToolCallsMade = toolCalls.ToList(),
            ElapsedTime = DateTime.UtcNow - startTime,
        });
    };
    
    process.Start();
    process.BeginOutputReadLine();
    process.BeginErrorReadLine();
    
    // Pipe the prompt to stdin then close it
    await process.StandardInput.WriteAsync(prompt);
    process.StandardInput.Close();
    
    // Wait for completion or cancellation
    try
    {
        await process.WaitForExitAsync(cancellationToken);
    }
    catch (OperationCanceledException)
    {
        process.Kill(entireProcessTree: true);
        return new CCExecutionResult
        {
            Success = false,
            Error = "Task cancelled by user",
            Duration = DateTime.UtcNow - startTime,
        };
    }
    
    var fullOutput = string.Join("\n", outputLines);
    var duration = DateTime.UtcNow - startTime;
    
    if (process.ExitCode != 0)
    {
        return new CCExecutionResult { Success = false, Error = $"CC exited with code {process.ExitCode}", Duration = duration };
    }
    
    // Detect if CC wrote an artifact file
    var artifactKey = await FindAndUploadArtifact(userId, taskId, cancellationToken);
    
    return new CCExecutionResult
    {
        Success = true,
        ArtifactS3Key = artifactKey.s3Key,
        ArtifactType = artifactKey.artifactType,
        Output = fullOutput.Length > 5000 ? fullOutput[^5000..] : fullOutput,
        Duration = duration,
    };
}

private string BuildPrompt(CCContextEnvelope envelope, string task)
{
    return $"""
    # User Context
    User ID: {envelope.UserId}
    User Name: {envelope.UserDisplayName}
    
    # Available Knowledge Bases
    {string.Join(", ", envelope.KbIds)}
    
    # Enabled MCP Servers
    {string.Join(", ", envelope.EnabledMcpServers)}
    
    {(envelope.MemorySummary != null ? $"# Memory Context\n{envelope.MemorySummary}\n" : "")}
    
    # Task Instructions
    {envelope.TaskInstructions}
    
    # Task
    {task}
    """;
}

private string GetUserWorkDir(string userId)
{
    // Use /tmp/cc-workspaces/{userId} as working directory for CC
    var dir = Path.Combine("/tmp/cc-workspaces", userId);
    Directory.CreateDirectory(dir);
    return dir;
}
```

### 3. `FindAndUploadArtifact` helper (in `FargateCCExecutionService`)

After CC completes, scan the user work dir for generated artifacts, upload to S3, return the key:
```csharp
private async Task<(string? s3Key, string? artifactType)> FindAndUploadArtifact(
    string userId, string taskId, CancellationToken ct)
{
    var workDir = GetUserWorkDir(userId);
    var extensions = new Dictionary<string, string>
    {
        [".docx"] = "docx", [".xlsx"] = "xlsx", [".pptx"] = "pptx",
        [".html"] = "html", [".json"] = "json",
        [".py"] = "code", [".js"] = "code", [".ts"] = "code", [".cs"] = "code",
    };
    
    foreach (var (ext, artifactType) in extensions)
    {
        var files = Directory.GetFiles(workDir, $"*{ext}", SearchOption.AllDirectories);
        if (files.Length == 0) continue;
        
        // Take the newest file
        var latest = files.OrderByDescending(f => File.GetLastWriteTimeUtc(f)).First();
        var fileName = Path.GetFileName(latest);
        var s3Key = $"workspaces/{userId}/artifacts/{taskId}/{fileName}";
        
        // Upload to S3
        using var fs = File.OpenRead(latest);
        await _s3Client.PutObjectAsync(new Amazon.S3.Model.PutObjectRequest
        {
            BucketName = _s3Bucket,
            Key = s3Key,
            InputStream = fs,
            ContentType = GetContentType(ext),
        }, ct);
        
        // Clean up work dir
        File.Delete(latest);
        
        return (s3Key, artifactType);
    }
    return (null, null);
}
```

### 4. Register in `Program.cs`

```csharp
builder.Services.AddScoped<ICCExecutionService, FargateCCExecutionService>();
```

Also wire in the S3 client if not already present:
```csharp
builder.Services.AddSingleton<IAmazonS3>(sp =>
{
    var region = Amazon.RegionEndpoint.GetBySystemName(
        sp.GetRequiredService<IConfiguration>()["AWS:Region"] ?? "us-east-1");
    return new AmazonS3Client(region);
});
```

### 5. Update `CCProgressHub.cs` (if it's a stub)

The hub at `Components/Hubs/CCProgressHub.cs` (or wherever it lives) should have a `SendProgress` method callable from the service. If it's already wired, leave it. If it's a stub, ensure it has:

```csharp
public async Task SendProgress(string userId, CCProgressUpdate update)
{
    await Clients.User(userId).SendAsync("ReceiveProgress", update);
}
```

### 6. Add `appsettings.json` keys

```json
{
  "CC": {
    "Model": "sonnet",
    "MaxDurationSeconds": 300
  },
  "AWS": {
    "Region": "us-east-1",
    "S3Bucket": "fortress-tools"
  }
}
```

---

## Constraints

- **Entra auth only** — no Cognito
- **GuidFormat=MySqlGuidFormat.None** on all DB connections (already set from earlier sprints)
- **varchar(36)** for GUID columns
- **CSS variables only** for any UI — no hardcoded colors
- `Dockerfile.debian` for the FAIT v2 image

---

## Acceptance Criteria

- [ ] `ICCExecutionService` interface defined with `DispatchTaskAsync`, `CCContextEnvelope`, `CCExecutionResult`, `CCProgressUpdate`
- [ ] `FargateCCExecutionService` implements `ICCExecutionService` using `Process` to spawn `claude --model sonnet --print --dangerously-skip-permissions`
- [ ] CC spawned with correct env vars (CLAUDE_CODE_ENTRYPOINT, etc.)
- [ ] Progress updates reported via `IProgress<CCProgressUpdate>` callback
- [ ] Cancel task kills CC child process
- [ ] Artifact detection scans for .docx/.xlsx/.pptx/.html/.json/code files
- [ ] Found artifacts uploaded to S3 `workspaces/{userId}/artifacts/{taskId}/`
- [ ] `ICCExecutionService` registered in `Program.cs`
- [ ] `dotnet build` succeeds

---

## ADO Tracking (MANDATORY)

After build complete:
```bash
mcporter call devops.add_comment --args '{
  "project": "Fortress",
  "id": 2857,
  "text": "**[Tony Stark — BUILD cycle 1]**\nCommit {hash}: {summary}. Build: SUCCEEDED."
}'
```

---

## Deliverables

1. `Services/ICCExecutionService.cs` (new)
2. `Services/FargateCCExecutionService.cs` (new)
3. `Program.cs` updated (service registrations)
4. Build Report: `/home/fredw/projects/fip/fait-v2/pipeline/ADO2857-BUILD-REPORT.md`
