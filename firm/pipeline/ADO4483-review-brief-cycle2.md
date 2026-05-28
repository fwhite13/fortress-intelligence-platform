# ADO#4483 Review Brief — Cycle 2

## Context
FIRM Mind Map tab feature. Cycle 1 identified 5 issues (2 Important, 3 Nitpick). This brief verifies all 5 were fixed in commit `fc64aa41`.

## Files to Read
1. `/home/fredw/projects/fip/firm/src/FortressIntelligenceRM.Web/Services/MindmapService.cs`
2. `/home/fredw/projects/fip/firm/src/FortressIntelligenceRM.Web/Components/Pages/MeetingDetail.razor`
3. `/home/fredw/projects/fip/firm/src/FortressIntelligenceRM.Web/Data/FirmDbContext.cs`

## Fix Verification Checklist

### I1 — S3 bucket config key
**Claim:** `Firm:KbS3Bucket` → `Firm:S3Bucket`, default `firm-recordings-dev`

Verify in `MindmapService.cs`:
- `BucketName` property uses `_config["Firm:S3Bucket"]` (NOT `Firm:KbS3Bucket`)
- Default fallback is `"firm-recordings-dev"` (NOT `"fortress-tools"` or any other string)
- `BucketName` is the ONLY way S3 bucket is referenced (no hardcoded strings elsewhere in the file)

### I2 — forceRegenerate parameter + DB-first cache logic
**Claim:** `forceRegenerate = false` default on interface + impl; DB lookup fires before Bedrock when `false`; `LoadMindmapAsync` passes `false`; `RegenerateMindmap` passes `true`

Verify in `MindmapService.cs` (interface `IMindmapService`):
- `GenerateAsync` signature: `Task<FirmMeetingMindmap?> GenerateAsync(long meetingId, bool forceRegenerate = false, CancellationToken ct = default)`

Verify in `MindmapService.cs` (class `MindmapService`):
- `GenerateAsync` impl has same signature with `forceRegenerate = false`
- When `forceRegenerate == false`: DB lookup (`db.Mindmaps.FirstOrDefaultAsync`) happens BEFORE any call to `InvokeBedrockAsync`
- When cached record found and `!forceRegenerate`: returns cached record WITHOUT calling Bedrock
- When `forceRegenerate == true`: skips the cache check and proceeds to Bedrock

Verify in `MeetingDetail.razor`:
- `LoadMindmapAsync` calls `MindmapService.GenerateAsync(_meeting.Id, forceRegenerate: false)`
- `RegenerateMindmap` calls `MindmapService.GenerateAsync(_meeting.Id, forceRegenerate: true)`

### I3 — Double-submit guard in RegenerateMindmap
**Claim:** `if (_mindmapLoading) return;` is the first statement in `RegenerateMindmap`

Verify in `MeetingDetail.razor`:
- `RegenerateMindmap` method starts with `if (_mindmapLoading) return;` as the FIRST statement
- No code executes before that guard (no state mutation, no assignment, nothing)

### N1 — S3 key prefix
**Claim:** `firm-transcripts/` → `firm-mindmaps/` in `SaveMindmapToS3Async` / `MirrorToS3Async`

Verify in `MindmapService.cs`:
- The S3 key string uses `firm-mindmaps/` prefix (NOT `firm-transcripts/`)
- Check ALL S3 PutObject or key construction in the file — no stale `firm-transcripts/` references remain

### N2 — FK constraint name
**Claim:** `HasConstraintName("fk_fmm_meeting")` → `HasConstraintName("fk_fmm_meeting_id")`

Verify in `FirmDbContext.cs`:
- The `FirmMeetingMindmap` entity configuration uses `.HasConstraintName("fk_fmm_meeting_id")`
- NOT `"fk_fmm_meeting"` (without `_id`)

## Regression Check
After verifying all 5 fixes, perform a quick regression sweep:

1. **No regressions in MeetingDetail.razor**: 
   - `LoadMindmapAsync` still has proper `_mindmapLoading = true/false` guards and `StateHasChanged` calls
   - The `OnMindmapTabActivated` / tab guard logic (`_mindmapTabOpened`) is intact and not broken
   - `RegenerateMindmap` has proper try/catch/finally and sets `_mindmapLoading = false` in `finally`

2. **No regressions in MindmapService.cs**:
   - The DB upsert logic (insert if new, update if existing) is still present after the cache-check block
   - `MirrorToS3Async` is still called (non-fatal) after DB save
   - `ExportFreeMindAsync` is unchanged

3. **Build status**: Tony reported 0 errors, 0 warnings. Note any contradicting evidence if found.

## Verdict Criteria
- **PASS**: All 5 fixes verified, no regressions, logic is sound
- **NEEDS-CHANGES**: ≥1 fix incomplete or regression found
- **FAIL**: Critical regression or fix makes things worse

Report findings per fix (✅ VERIFIED / ❌ NOT FIXED / ⚠️ PARTIAL) and give an overall verdict.
