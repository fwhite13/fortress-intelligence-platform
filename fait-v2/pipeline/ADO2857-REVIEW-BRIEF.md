# REVIEW Brief: ADO#2857 — FAIT v2 CC child process orchestration

**ADO WI:** #2857 (Fortress project)
**Review Cycle:** 1
**Build Commit:** `7eca7ade`

---

## MANDATORY: Use Claude Code CLI

```bash
CLAUDE_CODE_ENTRYPOINT=ado-pipeline \
CLAUDE_CODE_DISABLE_AUTO_MEMORY=1 \
CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1 \
CLAUDE_CODE_GLOB_TIMEOUT_SECONDS=30 \
cat /home/fredw/projects/fip/fait-v2/pipeline/ADO2857-REVIEW-BRIEF.md | \
claude --model sonnet --print --dangerously-skip-permissions
```

Working directory: `/home/fredw/projects/fip/fait-v2/`

---

## What Changed

Tony implemented the CC child process execution service:

- `Services/ICCExecutionService.cs` — interface + `CCContextEnvelope`, `CCExecutionResult`, `CCProgressUpdate` models
- `Services/FargateCCExecutionService.cs` — spawns `claude --model sonnet --print --dangerously-skip-permissions` as child process, streams progress, uploads artifacts to S3
- `Components/Hubs/CCProgressHub.cs` — SignalR hub for streaming progress to browser
- `Program.cs` — service registration + hub mapping
- `appsettings.json` — CC config keys

---

## Review Checklist

Use CC to read each file. Verify:

### Process Spawning
1. CC spawned with all 4 required env vars: `CLAUDE_CODE_ENTRYPOINT=ado-pipeline`, `CLAUDE_CODE_DISABLE_AUTO_MEMORY=1`, `CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1`, `CLAUDE_CODE_GLOB_TIMEOUT_SECONDS=30`
2. `RedirectStandardInput`, `RedirectStandardOutput`, `RedirectStandardError` all set to `true`
3. `UseShellExecute = false` (required for stream redirection)
4. Stdin closed after writing prompt (`process.StandardInput.Close()` or `await process.StandardInput.WriteAsync(...); process.StandardInput.Close()`)
5. `BeginOutputReadLine()` called before `WaitForExitAsync` — prevents deadlock on large output

### Cancellation & Error Handling
6. `CancellationToken` cancellation kills process with `process.Kill(entireProcessTree: true)` — not just `process.Kill()`
7. `process.ExitCode != 0` returns a failure `CCExecutionResult` with meaningful error message
8. `using var process` or explicit `process.Dispose()` — no process handle leaks
9. Exception handling wraps the whole execution — unhandled exceptions don't bubble up as 500s

### Artifact Upload
10. Artifact scan covers at minimum: `.docx`, `.xlsx`, `.pptx`, `.html`, `.json`, and at least one code extension
11. S3 upload uses AWS SDK (`IAmazonS3` or `AmazonS3Client`), not shell `aws` CLI
12. S3 key format: `workspaces/{userId}/artifacts/{taskId}/{filename}` — correct prefix
13. Artifact file is cleaned up from local disk after upload (prevents workspace pollution across tasks)

### SignalR Hub
14. `CCProgressHub` has a method callable from `FargateCCExecutionService` to push progress to a specific user
15. Hub registered in `Program.cs` with `app.MapHub<CCProgressHub>("/hubs/cc-progress")` or similar

### Service Registration
16. `ICCExecutionService` registered as `Scoped` or `Transient` in `Program.cs` — NOT singleton (process state is per-request)
17. `IAmazonS3` registered (singleton or via AWS SDK DI) if not already present

### Code Quality
18. No hardcoded S3 bucket names — uses config (`AWS:WorkspaceBucket` or similar)
19. No Cognito references
20. `dotnet build` 0 errors confirmed in build report

---

## ADO Tracking (MANDATORY)

After review complete:
```bash
mcporter call devops.add_comment --args '{
  "project": "Fortress",
  "id": 2857,
  "text": "**[Hawkeye — REVIEW cycle 1]**\nCode review {PASS|NEEDS-CHANGES}. Cycles: 1. {summary}"
}'
```

---

## Deliverables

1. Review Report: `/home/fredw/projects/fip/fait-v2/pipeline/ADO2857-REVIEW-REPORT-C1.md`
2. Verdict: PASS / NEEDS-CHANGES / FAIL
3. If NEEDS-CHANGES: file + line + exact fix
