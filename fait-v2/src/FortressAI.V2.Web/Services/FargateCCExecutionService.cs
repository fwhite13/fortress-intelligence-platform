using System.Diagnostics;
using Amazon.S3;
using Amazon.S3.Model;

namespace FortressAI.V2.Web.Services;

public class FargateCCExecutionService : ICCExecutionService
{
    private readonly IAmazonS3 _s3Client;
    private readonly IConfiguration _config;
    private readonly ILogger<FargateCCExecutionService> _logger;
    private readonly IContextEnvelopeService _contextEnvelopeService;

    public FargateCCExecutionService(
        IAmazonS3 s3Client,
        IConfiguration config,
        ILogger<FargateCCExecutionService> logger,
        IContextEnvelopeService contextEnvelopeService)
    {
        _s3Client = s3Client;
        _config = config;
        _logger = logger;
        _contextEnvelopeService = contextEnvelopeService;
    }

    public async Task<CCExecutionResult> DispatchTaskAsync(
        string userId,
        string task,
        CCContextEnvelope contextEnvelope,
        IProgress<CCProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var taskId = Guid.NewGuid().ToString("N")[..8];
        var startTime = DateTime.UtcNow;
        var model = _config["CC:Model"] ?? "sonnet";

        var systemClaudeMd = _contextEnvelopeService.GetSystemClaudeMd();
        var prompt = BuildPrompt(contextEnvelope, task, systemClaudeMd);

        var psi = new ProcessStartInfo
        {
            FileName = "claude",
            Arguments = $"--model {model} --print --dangerously-skip-permissions",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = GetUserWorkDir(userId),
        };

        psi.Environment["CLAUDE_CODE_ENTRYPOINT"] = "ado-pipeline";
        psi.Environment["CLAUDE_CODE_DISABLE_AUTO_MEMORY"] = "1";
        psi.Environment["CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR"] = "1";
        psi.Environment["CLAUDE_CODE_GLOB_TIMEOUT_SECONDS"] = "30";

        using var process = new Process { StartInfo = psi };
        var outputLines = new List<string>();
        var errorLines = new List<string>();
        var completedSteps = new List<string>();
        var toolCalls = new List<string>();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            outputLines.Add(e.Data);

            if (e.Data.StartsWith("Tool:") || e.Data.Contains("Called "))
                toolCalls.Add(e.Data);

            progress?.Report(new CCProgressUpdate
            {
                TaskId = taskId,
                CurrentStep = e.Data.Length > 100 ? e.Data[..100] + "..." : e.Data,
                CompletedSteps = completedSteps.ToList(),
                ToolCallsMade = toolCalls.ToList(),
                ElapsedTime = DateTime.UtcNow - startTime,
            });
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
                errorLines.Add(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.StandardInput.WriteAsync(prompt);
        process.StandardInput.Close();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already exited */ }
            _logger.LogWarning("CC task {TaskId} cancelled for user {UserId}", taskId, userId);
            return new CCExecutionResult
            {
                Success = false,
                Error = "Task cancelled by user",
                Duration = DateTime.UtcNow - startTime,
            };
        }

        var duration = DateTime.UtcNow - startTime;

        if (process.ExitCode != 0)
        {
            var stderr = string.Join("\n", errorLines);
            _logger.LogError("CC task {TaskId} failed with exit code {ExitCode}. Stderr: {Stderr}",
                taskId, process.ExitCode, stderr);
            return new CCExecutionResult
            {
                Success = false,
                Error = $"CC exited with code {process.ExitCode}",
                Duration = duration,
            };
        }

        var fullOutput = string.Join("\n", outputLines);
        var (s3Key, artifactType) = await FindAndUploadArtifact(userId, taskId, cancellationToken);

        _logger.LogInformation("CC task {TaskId} completed in {Duration}s for user {UserId}",
            taskId, duration.TotalSeconds, userId);

        return new CCExecutionResult
        {
            Success = true,
            ArtifactS3Key = s3Key,
            ArtifactType = artifactType,
            Output = fullOutput.Length > 5000 ? fullOutput[^5000..] : fullOutput,
            Duration = duration,
        };
    }

    private string BuildPrompt(CCContextEnvelope envelope, string task, string systemClaudeMd)
    {
        var kb = envelope.KbIds.Any()
            ? string.Join("\n", envelope.KbIds.Select(id => $"- {id}"))
            : "None assigned";
        var mcp = envelope.EnabledMcpServers.Any()
            ? string.Join("\n", envelope.EnabledMcpServers.Select(s => $"- {s}"))
            : "None enabled";
        var memory = envelope.MemorySummary != null
            ? $"## Memory Context\n{envelope.MemorySummary}\n\n"
            : "";

        return $"""
{systemClaudeMd}

---

# Per-User Context

## Identity
User ID: {envelope.UserId}
User Name: {envelope.UserDisplayName}

## Available Knowledge Bases
{kb}

## Enabled MCP Servers
{mcp}

{memory}## Task Instructions
{envelope.TaskInstructions}

## Task
{task}
""";
    }

    private string GetUserWorkDir(string userId)
    {
        var dir = Path.Combine("/tmp/cc-workspaces", userId);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private async Task<(string? s3Key, string? artifactType)> FindAndUploadArtifact(
        string userId, string taskId, CancellationToken ct)
    {
        var workDir = GetUserWorkDir(userId);
        var s3Bucket = _config["AWS:WorkspaceBucket"] ?? "fortress-user-workspaces";

        var extensions = new Dictionary<string, string>
        {
            [".docx"] = "docx",
            [".xlsx"] = "xlsx",
            [".pptx"] = "pptx",
            [".html"] = "html",
            [".json"] = "json",
            [".py"]   = "code",
            [".js"]   = "code",
            [".ts"]   = "code",
            [".cs"]   = "code",
        };

        foreach (var (ext, artifactType) in extensions)
        {
            var files = Directory.GetFiles(workDir, $"*{ext}", SearchOption.AllDirectories);
            if (files.Length == 0) continue;

            var latest = files.OrderByDescending(f => File.GetLastWriteTimeUtc(f)).First();
            var fileName = Path.GetFileName(latest);
            var s3Key = $"workspaces/{userId}/artifacts/{taskId}/{fileName}";

            await using var fs = File.OpenRead(latest);
            await _s3Client.PutObjectAsync(new PutObjectRequest
            {
                BucketName = s3Bucket,
                Key = s3Key,
                InputStream = fs,
                ContentType = GetContentType(ext),
            }, ct);

            File.Delete(latest);
            _logger.LogInformation("Uploaded artifact {S3Key} ({ArtifactType})", s3Key, artifactType);
            return (s3Key, artifactType);
        }

        return (null, null);
    }

    private static string GetContentType(string ext) => ext switch
    {
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        ".html" => "text/html",
        ".json" => "application/json",
        ".py"   => "text/x-python",
        ".js"   => "application/javascript",
        ".ts"   => "application/typescript",
        ".cs"   => "text/plain",
        _       => "application/octet-stream",
    };
}
