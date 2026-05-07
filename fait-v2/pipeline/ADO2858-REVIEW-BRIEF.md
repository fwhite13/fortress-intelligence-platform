# REVIEW Brief: ADO#2858 — FAIT v2 Workspace Explorer UI

**ADO WI:** #2858 (Fortress project)
**Review Cycle:** 1
**Build Commit:** `c3c242d`

---

## MANDATORY: Use Claude Code CLI

```bash
CLAUDE_CODE_ENTRYPOINT=ado-pipeline \
CLAUDE_CODE_DISABLE_AUTO_MEMORY=1 \
CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1 \
CLAUDE_CODE_GLOB_TIMEOUT_SECONDS=30 \
cat /home/fredw/projects/fip/fait-v2/pipeline/ADO2858-REVIEW-BRIEF.md | \
claude --model sonnet --print --dangerously-skip-permissions
```

Working directory: `/home/fredw/projects/fip/fait-v2/`

---

## What Changed

- `Services/IWorkspaceService.cs` — interface + `WorkspaceFolder`/`WorkspaceFile` models
- `Services/WorkspaceService.cs` — S3-backed implementation with pre-signed URLs, key-prefix validation
- `Components/Pages/Workspace.razor` — full UI: folder sidebar, file list, search, HTML preview, download, delete
- `wwwroot/css/app.css` — workspace styles
- `Program.cs` — `IWorkspaceService` registered as scoped

---

## Review Checklist

### Security
1. `GetDownloadUrlAsync` validates `s3Key.StartsWith($"workspaces/{userId}/")` before issuing pre-signed URL — no cross-user access possible
2. `DeleteFileAsync` has same prefix validation before deletion
3. `userId` comes from Entra OID claim in the Razor component — not from user input

### S3 / Service
4. Pre-signed URL expiry is reasonable (≤ 60 minutes) — 15 min is ideal
5. `ListObjectsV2Async` excludes the folder marker itself (key == prefix) — no phantom entries
6. All S3 operations use `IAmazonS3` (injected), not shell `aws` CLI
7. S3 bucket read from config — not hardcoded
8. `IWorkspaceService` registered as `Scoped` in `Program.cs`

### Razor / UI
9. No `@{ var x = ... }` declarations inside `@foreach` or `@if` markup blocks — locals in `@code` only
10. HTML preview uses `<iframe sandbox="allow-scripts">` — no unrestricted iframe execution
11. Delete confirmation dialog present before deletion
12. Search filter uses computed property or LINQ — no mutation in render loop
13. Empty state shown when folder has no files
14. CSS variables only — no hardcoded colors, font sizes, or spacing in `.razor` or `app.css` additions

### Build
15. `dotnet build` 0 errors confirmed

---

## ADO Tracking (MANDATORY)

After review complete:
```bash
mcporter call devops.add_comment --args '{
  "project": "Fortress",
  "id": 2858,
  "text": "**[Hawkeye — REVIEW cycle 1]**\nCode review {PASS|NEEDS-CHANGES}. Cycles: 1. {summary}"
}'
```

---

## Deliverables

1. Review Report: `/home/fredw/projects/fip/fait-v2/pipeline/ADO2858-REVIEW-REPORT-C1.md`
2. Verdict: PASS / NEEDS-CHANGES / FAIL
3. If NEEDS-CHANGES: file + line + exact fix
