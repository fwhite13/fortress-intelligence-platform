# BUILD Brief: ADO#2862 — FAIT v2: FIRM → FAIT v2 manual push

**ADO WI:** #2862 (Fortress project)
**Repos:** `fait-v2/` (receive endpoint) + `firm/` (Send button)
**Monorepo:** `/home/fredw/projects/fip/`
**Sprint:** FAIT v2 Sprint 4 — FAIT v1 Continuity

---

## MANDATORY: Use Claude Code CLI

```bash
CLAUDE_CODE_ENTRYPOINT=ado-pipeline \
CLAUDE_CODE_DISABLE_AUTO_MEMORY=1 \
CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1 \
CLAUDE_CODE_GLOB_TIMEOUT_SECONDS=30 \
cat /home/fredw/projects/fip/fait-v2/pipeline/ADO2862-BUILD-BRIEF.md | \
claude --model sonnet --print --dangerously-skip-permissions
```

Working directory: `/home/fredw/projects/fip/` (monorepo root)

---

## Context

FIRM (Fortress Intelligence Recording for Meetings) has meeting detail pages. This WI adds a "Send to FAIT v2 Assistant" button that pushes the meeting summary to the user's FAIT v2 chat as a new context message, so they can immediately discuss the meeting with their AI assistant.

**FIRM repo:** `/home/fredw/projects/fip/firm/src/FortressIntelligenceRM.Web/`
**FAIT v2 repo:** `/home/fredw/projects/fip/fait-v2/src/FortressAI.V2.Web/`

Both are Blazor Server apps sharing the same FIP Portal Aurora DB and Entra auth.

---

## Implementation

### Part 1: FAIT v2 — Receive endpoint

#### 1a. Add API endpoint `POST /api/agent/push-message`

In FAIT v2, add a controller/minimal API endpoint that accepts a message push from FIRM:

File: Add to `Program.cs` or a new `Controllers/AgentController.cs`

```csharp
// Minimal API endpoint (add to Program.cs or a controller)
app.MapPost("/api/agent/push-message", async (
    [FromBody] PushMessageRequest request,
    IUserAgentRuntime runtime,
    HttpContext httpContext,
    CancellationToken ct) =>
{
    // Validate the calling user is authenticated (Entra cookie auth)
    if (!httpContext.User.Identity?.IsAuthenticated ?? true)
        return Results.Unauthorized();
    
    var callerOid = httpContext.User.FindFirst("oid")?.Value 
                  ?? httpContext.User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;
    
    if (string.IsNullOrEmpty(callerOid))
        return Results.Unauthorized();
    
    // Ensure the user has a provisioned FAIT v2 account
    var status = await runtime.GetStatusAsync(callerOid, ct);
    if (status == AgentStatus.NotProvisioned)
        return Results.BadRequest(new { error = "User does not have a FAIT v2 account provisioned" });
    
    // Ensure the Fargate task is running
    await runtime.EnsureRunningAsync(callerOid, ct);
    
    // Store the pushed message in the conversations table as a new system message
    // This will appear in the chat UI on next load
    await StorePushedMessageAsync(callerOid, request, ct);
    
    return Results.Ok(new { success = true, message = "Message pushed to FAIT v2 assistant" });
}).RequireAuthorization();

// Request model
public record PushMessageRequest(
    string Source,       // "firm"
    string Title,        // Meeting title
    string Summary,      // Meeting summary text
    string? Transcript,  // Optional transcript excerpt (first 2000 chars)
    string MeetingId,    // FIRM meeting ID for reference
    DateTime MeetingDate
);
```

**`StorePushedMessageAsync` helper:** Insert a row into the `messages` table (shared Aurora DB) as a new "system" or "assistant" message in the user's active conversation, formatted as:

```
📋 **Meeting Summary: {Title}**
*{MeetingDate:MMM dd, yyyy}*

{Summary}

---
*Pushed from FIRM. Use this context to discuss the meeting with your assistant.*
```

If no active conversation exists, create a new one with title "Meeting: {Title}".

#### 1b. Verify `AgentStatus.NotProvisioned` exists in `IUserAgentRuntime`

Check `Services/IUserAgentRuntime.cs` — if `AgentStatus` enum doesn't have `NotProvisioned`, add it.

---

### Part 2: FIRM — Add "Send to FAIT v2 Assistant" button

**FIRM codebase:** `/home/fredw/projects/fip/firm/src/FortressIntelligenceRM.Web/`

First, find the meeting detail page/component. Look for:
- `Pages/MeetingDetail.razor` or similar
- The page that shows meeting summary, transcript, action items

#### 2a. Add the button to FIRM meeting detail

In the meeting detail component, add a button (visible to NexusAdmin role or meeting owner):

```razor
@if (CanSendToFaitV2)
{
    <MudButton Variant="Variant.Outlined" 
               Color="Color.Primary"
               StartIcon="@Icons.Material.Outlined.Send"
               OnClick="SendToFaitV2"
               Disabled="_isSending">
        @(_isSending ? "Sending..." : "Send to FAIT v2 Assistant")
    </MudButton>
    @if (_sendResult != null)
    {
        <MudAlert Severity="@(_sendSuccess ? Severity.Success : Severity.Error)" 
                  Dense="true">
            @_sendResult
        </MudAlert>
    }
}
```

#### 2b. Implement `SendToFaitV2` method

```csharp
private bool _isSending = false;
private string? _sendResult;
private bool _sendSuccess;

private bool CanSendToFaitV2 =>
    IsCurrentUserAdmin || Meeting?.OwnerId == CurrentUserId;

private async Task SendToFaitV2()
{
    _isSending = true;
    _sendResult = null;
    StateHasChanged();
    
    try
    {
        var payload = new
        {
            Source = "firm",
            Title = Meeting!.Title,
            Summary = Meeting.Summary ?? "No summary available",
            Transcript = Meeting.TranscriptText?.Length > 2000 
                ? Meeting.TranscriptText[..2000] + "..." 
                : Meeting.TranscriptText,
            MeetingId = Meeting.Id.ToString(),
            MeetingDate = Meeting.RecordedAt ?? Meeting.CreatedAt,
        };
        
        // POST to FAIT v2 API
        var faitV2BaseUrl = Configuration["FaitV2:BaseUrl"] ?? "https://fait-v2.dev.fortressam.ai";
        var response = await HttpClient.PostAsJsonAsync(
            $"{faitV2BaseUrl}/api/agent/push-message", payload);
        
        if (response.IsSuccessStatusCode)
        {
            _sendSuccess = true;
            _sendResult = "Meeting sent! Open FAIT v2 to discuss it with your assistant.";
        }
        else if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            _sendSuccess = false;
            _sendResult = "You don't have a FAIT v2 account yet. Contact your admin to get access.";
        }
        else
        {
            _sendSuccess = false;
            _sendResult = "Failed to send to FAIT v2. Please try again.";
        }
    }
    catch (Exception ex)
    {
        _sendSuccess = false;
        _sendResult = "Error connecting to FAIT v2.";
        Logger.LogError(ex, "Failed to push meeting {MeetingId} to FAIT v2", Meeting?.Id);
    }
    finally
    {
        _isSending = false;
        StateHasChanged();
    }
}
```

#### 2c. Add `FaitV2:BaseUrl` to FIRM `appsettings.json`

```json
{
  "FaitV2": {
    "BaseUrl": "https://fait-v2.dev.fortressam.ai"
  }
}
```

**Note:** The HTTP client call uses the user's existing session cookies — both FIRM and FAIT v2 use the same FIP shared cookie auth (`.FIPAuth` cookie on `.fortressam.ai` domain), so the request is automatically authenticated.

---

## Auth Note

Both FIRM and FAIT v2 use the FIP shared cookie consumer auth. The browser's `.FIPAuth` cookie covers both `firm.dev.fortressam.ai` and `fait-v2.dev.fortressam.ai` (both on `*.fortressam.ai`). The `HttpClient` call from FIRM frontend uses the user's session — the FAIT v2 endpoint validates via the existing cookie auth.

---

## Constraints

- **Entra auth only** — no Cognito
- **GuidFormat=MySqlGuidFormat.None** on all Aurora connections
- **varchar(36)** GUID columns — use `string` in C# models
- **CSS variables only** in Razor — no hardcoded colors/fonts/sizes
- No data stored beyond what already exists in FIRM meeting records
- Graceful error if user has no FAIT v2 account (don't crash, show message)

---

## Acceptance Criteria

- [ ] `POST /api/agent/push-message` endpoint in FAIT v2 (auth required)
- [ ] Endpoint validates user is authenticated, has FAIT v2 account
- [ ] Meeting summary stored as a new message in user's FAIT v2 conversation
- [ ] "Send to FAIT v2 Assistant" button in FIRM meeting detail
- [ ] Button visible only to meeting owner or NexusAdmin
- [ ] Success/error feedback shown to user in FIRM
- [ ] Works only if user has a provisioned FAIT v2 account (graceful error if not)
- [ ] No data stored beyond existing FIRM meeting records
- [ ] `dotnet build` succeeds for both FIRM and FAIT v2

---

## ADO Tracking (MANDATORY)

After build complete, add comment to ADO#2862:
```bash
mcporter call devops.add_comment --args '{
  "project": "Fortress",
  "id": 2862,
  "text": "**[Tony Stark — BUILD cycle 1]**\nCommit {hash}: {summary}. Build: SUCCEEDED. Changes in: firm/ (Send button) + fait-v2/ (push endpoint)."
}'
```

---

## Deliverables

1. FAIT v2: `POST /api/agent/push-message` endpoint
2. FIRM: "Send to FAIT v2 Assistant" button + `SendToFaitV2` implementation
3. Both `appsettings.json` files updated
4. Build Report: `/home/fredw/projects/fip/fait-v2/pipeline/ADO2862-BUILD-REPORT.md`
