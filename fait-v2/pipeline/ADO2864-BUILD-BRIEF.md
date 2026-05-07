# BUILD Brief: ADO#2864 — FAIT v2: In-app feedback submission with autonomous triage

**ADO WI:** #2864 (Fortress project)
**Repo:** `/home/fredw/projects/fip`
**Service:** `fait-v2/src/FortressAI.V2.Web/`
**Sprint:** FAIT v2 Sprint 4 — FAIT v1 Continuity

---

## MANDATORY: Use Claude Code CLI

```bash
CLAUDE_CODE_ENTRYPOINT=ado-pipeline \
CLAUDE_CODE_DISABLE_AUTO_MEMORY=1 \
CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1 \
CLAUDE_CODE_GLOB_TIMEOUT_SECONDS=30 \
cat /home/fredw/projects/fip/fait-v2/pipeline/ADO2864-BUILD-BRIEF.md | \
claude --model sonnet --print --dangerously-skip-permissions
```

Working directory: `/home/fredw/projects/fip/fait-v2/`

---

## Context

Users of FAIT v2 can report bugs and suggest features directly from within the app. Submissions are routed to Jarvis (the main orchestrator AI) via the OpenClaw sessions API for autonomous triage. Clear bugs are auto-dispatched to the pipeline; unclear/high-risk items are escalated to Fred via Discord DM.

This is a full-stack feature: Aurora DB table, API endpoint, Blazor UI component, SignalR feedback delivery.

---

## Implementation

### 1. Database: Add `feedback_submissions` table migration

Create a new EF Core migration that adds the `feedback_submissions` table to `FaitV2DbContext`:

**Model** `Data/Models/FeedbackSubmission.cs`:
```csharp
public class FeedbackSubmission
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..32]; // varchar(36) but no hyphens
    public string UserId { get; set; } = string.Empty;          // FK → users.Id
    public string Type { get; set; } = string.Empty;            // "bug" | "suggestion"
    public string Description { get; set; } = string.Empty;
    public string? PageUrl { get; set; }
    public string? ScreenshotS3Key { get; set; }
    public string Status { get; set; } = "pending";             // "pending" | "triaged" | "dispatched" | "escalated"
    public string? AdoWiId { get; set; }                        // ADO WI # if auto-dispatched
    public string? TriageResult { get; set; }                    // Jarvis triage response text
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? TriagedAt { get; set; }
}
```

Add to `FaitV2DbContext`:
```csharp
public DbSet<FeedbackSubmission> FeedbackSubmissions { get; set; }
```

Add `modelBuilder.Entity<FeedbackSubmission>()` config with `HasKey(f => f.Id)`.

**Migration:** Create and apply migration `AddFeedbackSubmissions`.

### 2. API Endpoints

#### `POST /api/feedback` — Submit feedback
```csharp
app.MapPost("/api/feedback", async (
    [FromBody] FeedbackRequest request,
    FaitV2DbContext db,
    IAmazonS3 s3,
    IConfiguration config,
    HttpContext httpContext,
    CancellationToken ct) =>
{
    var userId = GetUserId(httpContext);
    if (userId == null) return Results.Unauthorized();
    
    var submission = new FeedbackSubmission
    {
        UserId = userId,
        Type = request.Type,  // "bug" | "suggestion"
        Description = request.Description,
        PageUrl = request.PageUrl,
        Status = "pending",
    };
    
    // Handle screenshot upload if present
    if (request.ScreenshotBase64 != null)
    {
        var key = $"workspaces/system/feedback/{submission.Id}/screenshot.png";
        var bytes = Convert.FromBase64String(request.ScreenshotBase64);
        await s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = config["AWS:S3Bucket"] ?? "fortress-tools",
            Key = key,
            InputStream = new MemoryStream(bytes),
            ContentType = "image/png",
        }, ct);
        submission.ScreenshotS3Key = key;
    }
    
    db.FeedbackSubmissions.Add(submission);
    await db.SaveChangesAsync(ct);
    
    // Dispatch to Jarvis via OpenClaw sessions API (fire-and-forget)
    _ = DispatchToJarvisAsync(submission, config, ct);
    
    return Results.Ok(new { submissionId = submission.Id });
}).RequireAuthorization();
```

#### `POST /api/feedback/{id}/status` — Jarvis callback to update status
```csharp
app.MapPost("/api/feedback/{id}/status", async (
    string id,
    [FromBody] FeedbackStatusUpdate update,
    FaitV2DbContext db,
    IHubContext<CCProgressHub> hub,
    CancellationToken ct) =>
{
    // Simple shared secret validation
    // Jarvis sends X-Internal-Token header matching config value
    
    var submission = await db.FeedbackSubmissions.FindAsync([id], ct);
    if (submission == null) return Results.NotFound();
    
    submission.Status = update.Status;
    submission.AdoWiId = update.AdoWiId;
    submission.TriageResult = update.Message;
    submission.TriagedAt = DateTime.UtcNow;
    await db.SaveChangesAsync(ct);
    
    // Push result to user via SignalR
    var userMessage = update.Status switch
    {
        "dispatched" => $"Got it — this looks like a bug. It's been filed as ADO#{update.AdoWiId} and is already being worked on.",
        "escalated" => "Thanks — this one needs a closer look. Fred will review it shortly.",
        _ => update.Message ?? "Your feedback has been received.",
    };
    
    await hub.Clients.User(submission.UserId).SendAsync("ReceiveFeedbackResult", new
    {
        submissionId = id,
        status = update.Status,
        message = userMessage,
        adoWiId = update.AdoWiId,
    }, ct);
    
    return Results.Ok();
}).WithMetadata(new AllowAnonymousAttribute()); // Internal callback — validated by shared secret
```

#### `DispatchToJarvisAsync` helper (private method or service):
```csharp
private static async Task DispatchToJarvisAsync(FeedbackSubmission submission, IConfiguration config, CancellationToken ct)
{
    // Call OpenClaw sessions API to send message to Jarvis
    // POST to OpenClaw sessions send endpoint
    var ocBaseUrl = config["OpenClaw:BaseUrl"] ?? "http://localhost:3001";
    var ocToken = config["OpenClaw:ApiToken"];
    
    var payload = new
    {
        sessionKey = "agent:main:main",
        message = $"""
        ## FEEDBACK: {submission.Type.ToUpper()} from FAIT v2
        
        **Submission ID:** {submission.Id}
        **User ID:** {submission.UserId}
        **Page:** {submission.PageUrl ?? "unknown"}
        **Type:** {submission.Type}
        
        **Description:**
        {submission.Description}
        
        {(submission.ScreenshotS3Key != null ? $"**Screenshot:** s3://{submission.ScreenshotS3Key}" : "")}
        
        **Triage instructions:** 
        - Auto-dispatch if this is a clear UI bug, broken element, wrong data, or regression
        - Escalate to Fred if this involves auth/permissions, data integrity, scope-expanding features, or active WI duplicates
        - After triage, call back: POST https://fait-v2.dev.fortressam.ai/api/feedback/{submission.Id}/status
          with body: {{ "status": "dispatched"|"escalated", "adoWiId": "XXXX" (if dispatched), "message": "..." }}
        """,
    };
    
    try
    {
        using var http = new HttpClient();
        if (!string.IsNullOrEmpty(ocToken))
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ocToken);
        
        await http.PostAsJsonAsync($"{ocBaseUrl}/api/sessions/send", payload, ct);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[feedback] Failed to dispatch to Jarvis: {ex.Message}");
        // Non-fatal — submission is already saved
    }
}
```

### 3. Blazor UI: FeedbackModal component

File: `Components/Shared/FeedbackModal.razor`

```razor
@if (_isOpen)
{
    <MudDialog @bind-IsVisible="_isOpen" Options="_dialogOptions">
        <TitleContent>
            <MudText Typo="Typo.h6">
                @(_type == "bug" ? "Report a Bug" : "Suggest a Feature")
            </MudText>
        </TitleContent>
        <DialogContent>
            <MudToggleGroup T="string" @bind-Value="_type" SelectionMode="SelectionMode.SingleSelection">
                <MudToggleItem Value="@("bug")">🐛 Bug</MudToggleItem>
                <MudToggleItem Value="@("suggestion")">💡 Suggestion</MudToggleItem>
            </MudToggleGroup>
            
            <MudTextField @bind-Value="_description" Label="Describe the issue or idea" 
                          Lines="4" Required="true" Class="mt-3"
                          Placeholder="What happened? What did you expect?" />
            
            <MudText Typo="Typo.caption" Class="mt-2" Color="Color.Secondary">
                Current page: @_pageUrl
            </MudText>
        </DialogContent>
        <DialogActions>
            <MudButton OnClick="Close">Cancel</MudButton>
            <MudButton Variant="Variant.Filled" Color="Color.Primary" 
                       OnClick="Submit" Disabled="@(string.IsNullOrWhiteSpace(_description) || _isSubmitting)">
                @(_isSubmitting ? "Sending..." : "Submit")
            </MudButton>
        </DialogActions>
    </MudDialog>
}

@if (_showResult)
{
    <MudSnackbar>@_resultMessage</MudSnackbar>
}
```

Wire SignalR in the component to receive the `ReceiveFeedbackResult` callback from the server and show the outcome to the user.

### 4. Add persistent "Report a bug / Suggest a feature" button

In `MainLayout.razor` or the nav component, add a subtle button in the bottom-left nav:

```razor
<div class="feedback-trigger" @onclick="OpenFeedbackModal">
    <MudIcon Icon="@Icons.Material.Outlined.BugReport" Size="Size.Small" />
    <span>Report a bug</span>
</div>

<FeedbackModal @ref="_feedbackModal" />
```

Ensure the button is accessible from ALL pages.

### 5. Database migration

Add EF migration and apply:
```bash
cd src/FortressAI.V2.Web
dotnet ef migrations add AddFeedbackSubmissions
```

**Note:** Do NOT run `dotnet ef database update` — migrations run automatically on ECS container startup via `DatabaseInitializationService`.

---

## Config Keys to Add

`appsettings.json`:
```json
{
  "OpenClaw": {
    "BaseUrl": "http://localhost:3001",
    "ApiToken": ""
  },
  "Feedback": {
    "InternalToken": "fait-v2-internal-feedback-token"
  }
}
```

---

## Constraints

- **Entra auth only** — no Cognito
- **GuidFormat=MySqlGuidFormat.None** on ALL Aurora connections
- **varchar(36)** for GUID columns — use `string` type in C# models
- **CSS variables only** — no hardcoded colors/fonts/sizes in Razor
- Screenshot stored in S3 `workspaces/system/feedback/` prefix
- Jarvis dispatch is fire-and-forget (non-fatal if it fails)

---

## Acceptance Criteria

- [ ] `feedback_submissions` table in Aurora via EF migration
- [ ] `POST /api/feedback` stores submission, uploads screenshot to S3, dispatches to Jarvis
- [ ] `POST /api/feedback/{id}/status` callback endpoint (internal, shared secret)
- [ ] `FeedbackModal.razor` component with bug/suggestion toggle, text field
- [ ] Persistent feedback button accessible from all FAIT v2 pages
- [ ] SignalR push delivers triage result to user within ~10 seconds
- [ ] Auto-dispatch path: user sees ADO# in feedback
- [ ] Escalate path: Fred Discord DM triggered by Jarvis (FAIT v2 doesn't DM Fred directly — Jarvis does)
- [ ] `dotnet build` succeeds

---

## ADO Tracking (MANDATORY)

After build complete:
```bash
mcporter call devops.add_comment --args '{
  "project": "Fortress",
  "id": 2864,
  "text": "**[Tony Stark — BUILD cycle 1]**\nCommit {hash}: {summary}. Build: SUCCEEDED."
}'
```

---

## Deliverables

1. `Data/Models/FeedbackSubmission.cs` (new)
2. EF migration `AddFeedbackSubmissions`
3. API endpoints in `Program.cs`
4. `Components/Shared/FeedbackModal.razor` (new)
5. `MainLayout.razor` or nav component updated
6. Build Report: `/home/fredw/projects/fip/fait-v2/pipeline/ADO2864-BUILD-REPORT.md`
