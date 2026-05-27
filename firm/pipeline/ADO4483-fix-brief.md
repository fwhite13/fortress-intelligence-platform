# ADO#4483 — FIRM Mind Map Review Cycle 1 Fixes

Apply exactly these 5 fixes. No scope creep. No refactoring beyond what is listed.

---

## Fix I1 — Wrong config key for S3 bucket
File: `/home/fredw/projects/fip/firm/src/FortressIntelligenceRM.Web/Services/MindmapService.cs`

Change line ~31:
```diff
- private string BucketName => _config["Firm:KbS3Bucket"] ?? "fortress-tools";
+ private string BucketName => _config["Firm:S3Bucket"] ?? "firm-recordings-dev";
```

---

## Fix I2 — Unconditional Bedrock generation on every tab click
File: `/home/fredw/projects/fip/firm/src/FortressIntelligenceRM.Web/Services/MindmapService.cs`

1. Change the `IMindmapService` interface signature to add `forceRegenerate` parameter:
```csharp
Task<FirmMeetingMindmap?> GenerateAsync(long meetingId, bool forceRegenerate = false, CancellationToken ct = default);
```

2. Change the `GenerateAsync` method signature in `MindmapService` to:
```csharp
public async Task<FirmMeetingMindmap?> GenerateAsync(long meetingId, bool forceRegenerate = false, CancellationToken ct = default)
```

3. At the top of the `GenerateAsync` method body (after opening the db context, before building the prompt), add a DB-first check when `forceRegenerate == false`:
```csharp
// Return existing mindmap without hitting Bedrock unless forced
if (!forceRegenerate)
{
    var existing = await db.Mindmaps.FirstOrDefaultAsync(m => m.MeetingId == meetingId, ct);
    if (existing != null)
    {
        _logger.LogInformation("MindmapService: Returning cached mind map for meeting {MeetingId}", meetingId);
        return existing;
    }
}
```
Insert this AFTER `await using var db = await _dbFactory.CreateDbContextAsync(ct);` and BEFORE the `summary` lookup.

File: `/home/fredw/projects/fip/firm/src/FortressIntelligenceRM.Web/Components/Pages/MeetingDetail.razor`

4. In `LoadMindmapAsync`, change the call to `GenerateAsync` to pass `forceRegenerate: false`:
```csharp
var mindmap = await MindmapService.GenerateAsync(_meeting.Id, forceRegenerate: false);
```

5. In `RegenerateMindmap`, change it so it calls `GenerateAsync` directly with `forceRegenerate: true` instead of relying on the flag trick:
```csharp
private async Task RegenerateMindmap()
{
    if (_mindmapLoading) return;
    _mindmapJson = null;
    _mindmapError = null;
    _mindmapLoading = true;
    StateHasChanged();
    try
    {
        var mindmap = await MindmapService.GenerateAsync(_meeting.Id, forceRegenerate: true);
        if (mindmap != null)
        {
            _mindmapJson = mindmap.MindmapJson;
            _mindmapLoading = false;
            await InvokeAsync(StateHasChanged);
            await Task.Delay(200);
            await JS.InvokeVoidAsync("firmMindmap.render", "mindmap-container", _mindmapJson);
        }
    }
    catch (Exception ex)
    {
        Logger.LogError(ex, "FIRM MeetingDetail: mind map regeneration failed for meeting {Id}", _meeting.Id);
        _mindmapError = "Failed to regenerate mind map.";
    }
    finally
    {
        _mindmapLoading = false;
        StateHasChanged();
    }
}
```

---

## Fix I3 — No double-submit guard on RegenerateMindmap
File: `/home/fredw/projects/fip/firm/src/FortressIntelligenceRM.Web/Components/Pages/MeetingDetail.razor`

The `RegenerateMindmap` method rewritten in I2 already includes `if (_mindmapLoading) return;` as the first line. This fix is satisfied by I2.

---

## Fix N1 — Wrong S3 key prefix in MirrorToS3Async
File: `/home/fredw/projects/fip/firm/src/FortressIntelligenceRM.Web/Services/MindmapService.cs`

In `MirrorToS3Async`, change the S3 key prefix from `firm-transcripts/` to `firm-mindmaps/`:
```diff
- var key = $"firm-transcripts/{meetingId}/mindmap.json";
+ var key = $"firm-mindmaps/{meetingId}/mindmap.json";
```

---

## Fix N2 — FK name mismatch in FirmDbContext
File: `/home/fredw/projects/fip/firm/src/FortressIntelligenceRM.Web/Data/FirmDbContext.cs`

Change line ~75:
```diff
- .HasConstraintName("fk_fmm_meeting");
+ .HasConstraintName("fk_fmm_meeting_id");
```

---

## Important Notes
- Do NOT change anything other than what is listed above
- Do NOT add using statements that are already present
- Do NOT change any other methods
- Preserve all existing logging, error handling, and structure
- After making all changes, verify the files compile logically (interface and implementation must match signatures)
