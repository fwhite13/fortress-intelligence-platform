## Build Report — ADO#5111
**WI:** FAIT file preview cache not invalidated on new file version
**Date:** 2026-06-11
**Status:** COMPLETE

### CC Invocation
```bash
export CLAUDE_CODE_ENTRYPOINT=ado-pipeline
export CLAUDE_CODE_DISABLE_AUTO_MEMORY=1
export CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1
export CLAUDE_CODE_GLOB_TIMEOUT_SECONDS=30

cat /tmp/brief-5111.md | claude \
  --model sonnet \
  --output-format stream-json \
  --verbose \
  --print \
  --dangerously-skip-permissions
```

### Goal Condition
`Cache key includes artifact version/record ID; uploading a new version of an existing file triggers preview regeneration (not served from cache); repeated views of the same version do not regenerate, or stop after 20 turns`

### Goal Outcome
ACHIEVED (33 turns)

CC confirmed all three conditions with a logical trace-through:
1. Cache hit now gates on `PreviewVersion == CurrentVersion` — version is part of validity check
2. `SaveUploadAsync` nulls both `PreviewS3Key` and `PreviewVersion` on new version upload (defense in depth: explicit null-out + version mismatch both trigger regeneration)
3. After successful conversion, `PreviewVersion = CurrentVersion` is saved — repeated views hit cache

### Files Modified
- `fait/src/FortressAI.Shared/Models/WorkspaceUpload.cs` — added `PreviewVersion int?` property with `[Column("preview_version")]`
- `fait/src/FortressAI.Web/Services/WorkspaceUploadService.cs` — nulls `PreviewVersion` alongside `PreviewS3Key` in `SaveUploadAsync` (new version upload) and `RollbackFileAsync`
- `fait/src/FortressAI.Web/Services/ArtifactPreviewService.cs` — cache check now requires `PreviewVersion == CurrentVersion` in both `ConvertPptxAsync` and `ConvertXlsxAsync`; sets `PreviewVersion = CurrentVersion` on conversion success; added `[preview]` hit/miss log lines
- `fait/src/FortressAI.Web/Controllers/ArtifactPreviewController.cs` — same version-gated cache check and setter in `ConvertPptx`
- `fait/src/FortressAI.Web/Migrations/20260611000000_AddPreviewVersionToWorkspaceUploads.cs` — new migration adding `preview_version INT NULL`
- `fait/src/FortressAI.Web/Migrations/AppDbContextModelSnapshot.cs` — snapshot updated with `PreviewVersion`

### Root Cause
Same `WorkspaceUpload` record is updated in-place when a new version is uploaded (`SaveUploadAsync` mutates `existingFile`). `PreviewS3Key` was never cleared on version increment, so the stale cached preview was always returned.

### Self-Review Checklist
- [x] All ACs verified — version-gated cache check, new version clears cache, repeated views hit cache
- [x] No hardcoded values
- [x] Error handling preserved — null-safe `upload?.CurrentVersion` guard in logging
- [x] No debug artifacts left behind
- [x] Build verified clean (0 errors) with `dotnet build`
