# ADO#4483 — FIRM: Restore Mind Map Tab — Adversarial Code Review Brief

You are performing an adversarial code review of a 12-file merge that restores the Mind Map tab
from an orphaned branch into main. Your job is to find real bugs, consistency mismatches, security
issues, and logic errors. Be skeptical. Don't take the build report's word for it.

## Files to Review

All files are at `/home/fredw/projects/fip/firm/src/FortressIntelligenceRM.Web/`:

1. `Services/MindmapService.cs`
2. `Components/Pages/MeetingDetail.razor`
3. `Models/FirmMeetingMindmap.cs`
4. `Controllers/MeetingsApiController.cs` (focus on mindmap endpoints ~line 1033–1090)
5. `Data/DatabaseInitializationService.cs` (focus on firm_meeting_mindmaps ~line 162–245)
6. `Data/FirmDbContext.cs`
7. `Services/S3Service.cs` (for S3 key pattern comparison)
8. `appsettings.json`
9. `wwwroot/js/firm-utils.js`

## Acceptance Criteria (verify each)

1. Mind Map tab appears on meeting detail for Complete meetings
2. Generate Mind Map button triggers Bedrock generation
3. mind-elixir renders the map
4. Regenerate + Export .mm work
5. firm_meeting_mindmaps table auto-created in Aurora

## Specific Issues to Investigate

### 1. MindmapService.cs — Bedrock + S3 + DB

**A. Bedrock model ID config key:**
- `MindmapService` reads model ID from `_config.GetValue<string>("Bedrock:SummaryModelId", ...)`
- This is the SAME key used by the summary service (sharing a model config key between summarization and mindmap generation)
- Is this intentional or an oversight? Should there be a dedicated `Bedrock:MindmapModelId`?
- The fallback is `"anthropic.claude-3-sonnet-20240229-v1:0"` — but appsettings.json has `"us.anthropic.claude-sonnet-4-6"`. Verify the fallback is irrelevant in prod (config is set).

**B. S3 bucket key mismatch — CRITICAL CHECK:**
- `MindmapService.MirrorToS3Async` uses: `BucketName = _config["Firm:KbS3Bucket"] ?? "fortress-tools"`
- `S3Service` uses: `_config["Firm:S3Bucket"] ?? "firm-recordings-dev"`
- `appsettings.json` has: `"Firm": { "S3Bucket": "firm-recordings-dev" }` — NO `KbS3Bucket` key present
- **This means MindmapService will always use the fallback bucket `"fortress-tools"` in all environments where `Firm:KbS3Bucket` is not set.**
- The other S3 artifacts (transcripts, audio) go to `Firm:S3Bucket`. Mindmaps go to a different bucket (or the wrong one).
- Is `"fortress-tools"` a valid bucket? Does it exist? Is this consistent with other FIRM S3 artifacts?
- Read the S3 key pattern: `firm-transcripts/{meetingId}/mindmap.json` — note the path prefix says `firm-transcripts` but it's a mindmap, not a transcript. Is this intentional alignment with the transcript S3 path, or a copy-paste error?

**C. Fire-and-forget S3 mirror — unhandled exceptions:**
- `_ = MirrorToS3Async(meetingId, mindmapJson)` fires without awaiting
- `MirrorToS3Async` has an internal try/catch, so it won't crash the app
- But: any exception in the outer `async Task` after the fire-and-forget won't propagate — this is safe
- Confirm: the fire-and-forget is truly non-fatal and acceptable

**D. Service lifetime — Scoped vs Singleton:**
- `MindmapService` is registered as `AddScoped` (line 107 of Program.cs)
- It uses `IDbContextFactory<FirmDbContext>` (correct for scoped/per-request), `IAmazonBedrockRuntime`, `IAmazonS3`, `IConfiguration`, `ILogger`
- `IAmazonS3` is registered as... check if S3Service is singleton. If `IAmazonS3` is injected as singleton into a scoped service, is there a captured dependency problem?
- Look at how other scoped services handle IAmazonS3 injection.

**E. Double-generation race condition:**
- `POST /api/meetings/{id}/generate-mindmap` fires `_ = _mindmapService.GenerateAsync(id)` (fire-and-forget, returns 202)
- If two POST requests hit simultaneously, two Bedrock calls fire at once
- Both will try to upsert the same meeting_id row — the upsert logic does FirstOrDefault then either update or insert
- Is there a race condition here where both reads return null, both try to INSERT, and one gets a unique constraint violation on `meeting_id`?
- The outer catch in GenerateAsync catches all exceptions and logs, so the unique constraint violation would be silently swallowed
- But: is the `meeting_id UNIQUE` constraint sufficient protection? One will fail silently. Is this acceptable?

**F. LoadMindmapAsync calls GenerateAsync directly:**
- In MeetingDetail.razor, `LoadMindmapAsync` calls `MindmapService.GenerateAsync` on every tab open
- If a mindmap already exists, it regenerates it every time! Look at the code: it fetches summary, generates via Bedrock, upserts — there is NO check for "does a mindmap already exist before calling Bedrock"
- This means every time a user clicks the Mind Map tab, it calls Bedrock unnecessarily
- Confirm: is there a "check DB first, only generate if missing" path? Or does it always regenerate?

### 2. MeetingDetail.razor — Mind Map tab

**A. Tab completion guard:**
- The Mind Map tab content is guarded by `@if (_meeting!.Status != MeetingStatus.Complete)`
- WAIT — this logic is INVERTED: it shows "not available" when status is NOT Complete, and renders the mind map block when it IS Complete. Read the code carefully.
- Does the tab correctly show mind map content only for Complete meetings?

**B. JS interop timing:**
- `LoadMindmapAsync` sets `_mindmapLoading = false` then calls `StateHasChanged` before `Task.Delay(200)` then calls `JS.InvokeVoidAsync("firmMindmap.render", ...)`
- The `#mindmap-container` div uses `display:none` when `_mindmapJson == null`, but at the time `StateHasChanged` is called, `_mindmapJson` is set
- Check: is there a SECOND `StateHasChanged()` in the finally block that could re-hide the container after JS renders it?
- The finally block does `_mindmapLoading = false; StateHasChanged()` — at this point `_mindmapJson` is already set, so the container remains visible. This should be safe, but verify.

**C. Double-submit protection:**
- `RegenerateMindmap` does NOT disable a button during generation — it sets `_mindmapJson = null` and `_mindmapTabOpened = false` then calls `LoadMindmapAsync`
- During generation, `_mindmapLoading = true` which shows a progress bar but the Regenerate button may still be clickable in the error state
- Actually: while loading, the template only shows `MudProgressLinear`, so the Regenerate button is only visible in the error state — but if user clicks Regenerate while already loading, could two concurrent `LoadMindmapAsync` calls run?
- Check: is there a `_mindmapLoading` guard in `RegenerateMindmap`?

**D. Cleanup on navigation:**
- When user navigates away from the MeetingDetail page, the `mind-elixir` instance (`window.firmMindmap._instance`) is NOT destroyed
- Does `MeetingDetail.razor` implement `IAsyncDisposable` or `IDisposable` to call a JS cleanup function?
- What happens if the user opens another meeting's Mind Map tab — does the old instance interfere?

**E. OnMindMapTabSelected vs OnAfterRenderAsync:**
- `OnMindMapTabSelected` is called on tab click via `OnClick="OnMindMapTabSelected"`
- This fires from the UI event, not from `OnAfterRenderAsync` — so the DOM may not have re-rendered when JS interop is called
- The code has `await Task.Delay(200)` as a workaround — this is fragile but probably works in practice
- Note: `_mindmapLoading = false` + `await InvokeAsync(StateHasChanged)` + `await Task.Delay(200)` is the full sequence before the JS call

### 3. MeetingsApiController.cs — mindmap endpoints

**A. GET /mindmap returns 404 or data:**
- Returns `NotFound` if no mindmap, `Ok` with mindmap data if found — correct behavior

**B. POST /generate-mindmap — fire and forget without cancellation:**
- `_ = _mindmapService.GenerateAsync(id)` — the HttpContext may be disposed before GenerateAsync completes
- `MindmapService.GenerateAsync` accepts a `CancellationToken ct = default` — since no CT is passed from the controller, it uses `default` (never cancelled)
- This is the correct pattern for fire-and-forget; no issue here

**C. Export endpoint — Content-Disposition:**
- Returns `File(bytes, "application/xml", filename)` — the `File` overload with a filename sets `Content-Disposition: attachment; filename=...` automatically in ASP.NET Core
- Is the filename slug safe? It runs `Regex.Replace(slug, @"[^a-z0-9\-]", "")` — this is safe
- Is the slug empty-string possible? If title is all special characters, slug becomes empty string, resulting in filename `-mindmap.mm`. Minor issue.

**D. ExportMindmap ownership check — double verification:**
- The controller calls `ResolveOwnedMeetingWithUser` for ownership check, then `ExportFreeMindAsync(id, user!.Id)` which ALSO does an ownership check internally
- This is redundant but not harmful — it just means two DB queries instead of one

### 4. DatabaseInitializationService.cs

**A. firm_meeting_mindmaps table DDL:**
- `id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY` ✓
- `meeting_id BIGINT NOT NULL UNIQUE` ✓  
- `mindmap_json LONGTEXT NOT NULL` ✓
- `model_used VARCHAR(100) NULL` ✓
- `created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP` ✓
- FK constraint in alterStatements: `ADD CONSTRAINT fk_fmm_meeting_id FOREIGN KEY (meeting_id) REFERENCES firm_meetings(id) ON DELETE CASCADE` ✓
- Error handling catches 1060, 1061, 1091, 1826 codes ✓
- No `ADD COLUMN IF NOT EXISTS` in alters ✓

**B. EF DbContext vs DB schema alignment — MANDATORY CHECK:**
- `FirmDbContext` maps `FirmMeetingMindmap` to `firm_meeting_mindmaps` table
- EF property mappings vs DB column names:
  - `Id` → `id` (via `ValueGeneratedOnAdd()` — no explicit HasColumnName)
  - `MeetingId` → `meeting_id` (via HasColumnName)
  - `MindmapJson` → `mindmap_json` (via HasColumnName)
  - `ModelUsed` → `model_used` (via HasColumnName)  
  - `CreatedAt` → `created_at` (via HasDefaultValueSql)
- QUESTION: `Id` property has NO `HasColumnName` mapping. EF Core snake_case convention (via Pomelo) should map `Id` → `id`. Does FIRM use Pomelo's snake_case naming convention? Check OnModelCreating or DbContext options for `.UseSnakeCaseNamingConvention()`.
- If no snake_case convention is active AND no HasColumnName on Id, EF may try column `Id` (Pascal case) but DB has `id` — on MySQL this is case-insensitive, so probably fine. But verify the pattern matches other entities.

### 5. firm-utils.js — mind-elixir integration

**A. CDN dependency — no fallback:**
- `import('https://cdn.jsdelivr.net/npm/mind-elixir@4/dist/MindElixir.js')` — CDN with no fallback
- If jsDelivr is down, mind maps silently fail (the error propagates as a JS exception in `render()`)
- The Blazor component catches the JSException at the C# level and sets `_mindmapError`
- Is this acceptable? It's non-fatal but means mind maps don't work if CDN is unavailable.

**B. XSS risk — meeting content rendered into mind map:**
- `_toMindElixirData` parses JSON from the server and sets `topic: node.label` on mind-elixir nodes
- mind-elixir renders these as DOM text nodes (not innerHTML), which is safe
- But: if mind-elixir renders via innerHTML internally, and meeting titles/summaries contain HTML, there could be XSS
- Verify: does mind-elixir@4 render node labels as textContent or innerHTML?

**C. null/undefined data handling:**
- `_toMindElixirData` handles null mindmapJson: `(typeof mindmapJson === 'string') ? JSON.parse(mindmapJson) : mindmapJson`
- If `mindmapJson` is null/undefined, `JSON.parse(null)` returns null, then `null.title` throws
- In practice, the C# code never calls `firmMindmap.render` with null — it only calls after confirming `_mindmapJson != null`
- But if it were called with null, the exception would propagate to C# and show the error state. Non-fatal.

**D. mind-elixir instance cleanup:**
- `window.firmMindmap._instance` is set globally
- If user navigates to a different meeting and opens the Mind Map tab, `_instance.destroy?.()` is called — this should clean up
- But the old container (from a previous meeting page) no longer exists in DOM — `destroy()` on a detached element may throw silently

## Pass/Fail Criteria

**FAIL-level issues:**
- Any Bedrock call that will fail in production due to wrong config key
- Any DB write that will fail due to schema mismatch
- Security vulnerabilities (auth bypass, XSS)
- Race condition that causes data corruption

**NEEDS-CHANGES-level issues:**
- Wrong S3 bucket config key causing mindmaps to land in wrong bucket
- LoadMindmapAsync always calling Bedrock even when mindmap exists (expensive)
- Missing double-submit guard in RegenerateMindmap
- JS cleanup on navigation

**PASS-level observations (note but don't block):**
- Shared model ID config key between summary and mindmap
- CDN dependency with no fallback
- Redundant ownership check in export

## Report Format

For each issue found:
- Severity: Critical / Important / Nitpick
- File and line number
- Exact problem description
- Evidence (code snippet)
- Impact
- Recommended fix

Be specific. Cite actual code. Distinguish between "will definitely break in production" and "might cause issues under certain conditions."
