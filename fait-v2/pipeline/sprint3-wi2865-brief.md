# BUILD BRIEF — ADO#2865 — Google Stitch Design Agent
**Sprint 3, Lane 2 | FAIT v2 Epic #2835 | §6.3 Design Agent**

## Context
You are Tony Stark (software-engineer). You are implementing FAIT v2 Sprint 3, WI #2865.
FAIT v2 repo: `~/projects/fip/fait-v2/` | branch: `main`
Spec: `memory/projects/fait-v2-spec-2026-04-27.md` (§6.3 Design Agent)

## What Was Built (Sprint 2, now on main)
- `FortressAI.V2.Web` Blazor Server app
- `IUserAgentRuntime` / `FargateUserAgentRuntime` (per-user Fargate harness)
- `IMemoryFileService` (S3-backed memory files)
- `ChatView.razor`, `MessageBubble.razor`, `DualPaneLayout.razor`
- `AssistantLoadingState.razor` (cold start UX)
- `fortress.css` + `FipTheme.cs` CSS-variable-driven UI
- `Components/Pages/Connectors.razor` (currently a placeholder)

## Objective
Build the Design Agent service layer in FAIT v2. The Design Agent is a **first-class built-in agent** that creates and iterates on visual HTML/CSS deliverables using Google Stitch MCP. It is invokable from the main assistant chat.

**Note:** WI #2866 (Stitch MCP wiring into Fargate harness) runs in parallel and is a dependency. This WI (#2865) focuses on the Blazor-side Design Agent service layer and UI components. The Stitch MCP calls themselves are placed via the Fargate harness (which #2866 wires). You build the service abstraction and UI — the harness integration is #2866's job.

## What to Build

### 1. `IDesignAgentService` + `DesignAgentService`

Create `Services/IDesignAgentService.cs` and `Services/DesignAgentService.cs`.

```csharp
public interface IDesignAgentService
{
    /// <summary>
    /// Generate an HTML screen from a text prompt via Stitch.
    /// Returns the generated HTML content.
    /// </summary>
    Task<DesignAgentResult> GenerateScreenAsync(string userId, string prompt, string? designDnaContext = null, CancellationToken ct = default);

    /// <summary>
    /// Extract design DNA (colors, fonts, layout) from an existing screen HTML or image.
    /// </summary>
    Task<string> ExtractDesignContextAsync(string userId, string screenHtmlOrImageBase64, CancellationToken ct = default);

    /// <summary>
    /// Iteratively refine an existing screen with a follow-up prompt.
    /// </summary>
    Task<DesignAgentResult> RefineScreenAsync(string userId, string existingScreenId, string refinementPrompt, CancellationToken ct = default);

    /// <summary>
    /// Save a generated artifact to S3 under workspaces/{userId}/artifacts/design/{sessionId}/
    /// Returns the S3 key.
    /// </summary>
    Task<string> SaveArtifactAsync(string userId, string html, string artifactName, CancellationToken ct = default);

    /// <summary>Is Stitch available? Returns false if GCP credentials not configured or health check fails.</summary>
    Task<bool> IsStitchAvailableAsync(CancellationToken ct = default);
}

public record DesignAgentResult(
    string Html,
    string? ScreenId,     // Stitch screen ID if returned
    string? ProjectId,    // Stitch project ID if returned
    bool IsFallback       // true = CC-native HTML, Stitch was unavailable
);
```

**Implementation strategy:**
- `DesignAgentService` dispatches Stitch tool calls via `IUserAgentRuntime.SendToolCallAsync()` (or an equivalent harness dispatch method — see note below)
- If Stitch is unavailable (`IsStitchAvailableAsync()` returns false), fall back to asking the Fargate harness to generate HTML natively via CC (set `IsFallback = true` in result)
- Save artifacts to S3 via `IAmazonS3`

**Harness dispatch note:** The Fargate harness (#2866) will expose Stitch MCP tools. Call them by adding a method to `IUserAgentRuntime`:
```csharp
// Add to IUserAgentRuntime.cs:
Task<string> DispatchToolCallAsync(string userId, string toolName, Dictionary<string, object> args, CancellationToken ct = default);
```
And stub implementation in `FargateUserAgentRuntime`:
```csharp
public async Task<string> DispatchToolCallAsync(string userId, string toolName, Dictionary<string, object> args, CancellationToken ct = default)
{
    // POST to harness endpoint: /tools/{toolName} with args as JSON body
    // Return raw result JSON
    var harness = await GetOrStartHarnessAsync(userId, ct);
    var response = await _httpClient.PostAsJsonAsync($"{harness.BaseUrl}/tools/{toolName}", args, ct);
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadAsStringAsync(ct);
}
```

### 2. DB Migration: `design_agent_sessions` table

```sql
CREATE TABLE design_agent_sessions (
  id              CHAR(36)     NOT NULL PRIMARY KEY,
  user_id         CHAR(36)     NOT NULL,
  conversation_id CHAR(36),                        -- link to parent conversation if applicable
  stitch_project_id VARCHAR(200),
  design_dna      TEXT,                            -- extracted design DNA JSON
  created_at      DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  updated_at      DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
  INDEX ix_design_agent_sessions_user_id (user_id),
  CONSTRAINT fk_design_sessions_user FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE design_agent_artifacts (
  id              CHAR(36)     NOT NULL PRIMARY KEY,
  session_id      CHAR(36)     NOT NULL,
  user_id         CHAR(36)     NOT NULL,
  artifact_name   VARCHAR(200) NOT NULL,
  s3_key          VARCHAR(500) NOT NULL,
  stitch_screen_id VARCHAR(200),
  is_fallback     TINYINT(1)   NOT NULL DEFAULT 0,
  created_at      DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  INDEX ix_design_artifacts_session_id (session_id),
  INDEX ix_design_artifacts_user_id (user_id),
  CONSTRAINT fk_design_artifacts_session FOREIGN KEY (session_id) REFERENCES design_agent_sessions(id) ON DELETE CASCADE
) ENGINE=InnoDB;
```

Add EF models `DesignAgentSession.cs` and `DesignAgentArtifact.cs` in `Data/Models/`. Register in `FaitV2DbContext`. Follow existing conventions.

**GuidFormat rule:** `GuidFormat = MySqlGuidFormat.None` on ALL DB connections. varchar(36) for GUID columns.

### 3. Design Agent UI Components

**`Components/Agent/DesignAgentView.razor`**
- Chat interface specialized for the Design Agent
- Shows Design Agent badge/indicator in header
- Supports image upload (user can drag/drop an image to use as reference)
- Rendered HTML preview inline (iframe, sandboxed) or in dual-pane when available
- Shows "Stitch unavailable — using CC-native generation" notice when `IsFallback = true`
- Iteration support: follow-up prompts appear as new turns, updating the preview

**`Components/Agent/DesignArtifactCard.razor`**
- Artifact card component shown in chat history after a design is generated
- Shows: artifact thumbnail (iframe at small scale), artifact name, "Open in Preview" button, "Download HTML" button
- "Open in Preview" triggers dual-pane layout with full artifact preview

**`Components/Agent/AgentPluginBadge.razor`**
- Simple badge component used in chat header to indicate active agent
- Props: `AgentName` (string), `IsActive` (bool)
- Shows colored badge when `IsActive = true` (uses `--color-accent-bg` CSS variable)

### 4. Design Agent invocation from ChatView

In `ChatView.razor`, add agent selector support:
- Add a `ActiveAgent` enum: `MainAssistant | DesignAgent`
- Add an "Agent" selector button in the chat toolbar (MudIconButton with a palette icon)
- When `DesignAgent` is active: render `DesignAgentView.razor` instead of the default message list
- Show `AgentPluginBadge` in chat header with "Design Agent" label
- Route user messages through `IDesignAgentService` instead of direct harness dispatch
- When user returns to `MainAssistant`, agent indicator disappears

### 5. Register in Program.cs

```csharp
builder.Services.AddScoped<IDesignAgentService, DesignAgentService>();
```

### 6. Acceptance Criteria
- [ ] `IDesignAgentService` and `DesignAgentService` implemented and registered
- [ ] `DispatchToolCallAsync` added to `IUserAgentRuntime` and stubbed in `FargateUserAgentRuntime`
- [ ] `design_agent_sessions` and `design_agent_artifacts` DB tables created via EF migration
- [ ] `DesignAgentView.razor` renders: text prompt → calls `GenerateScreenAsync` → iframe preview shown
- [ ] Image upload in `DesignAgentView` triggers `ExtractDesignContextAsync` before generation
- [ ] `DesignArtifactCard.razor` shows after generation with Download + Preview buttons
- [ ] `AgentPluginBadge.razor` shows in chat header when Design Agent active
- [ ] Fallback to CC-native HTML when Stitch unavailable, with visible notice to user
- [ ] Generated HTML saved to S3 `workspaces/{userId}/artifacts/design/{sessionId}/`
- [ ] Design Agent invokable from chat toolbar in `ChatView.razor`
- [ ] ALL styling via CSS variables — zero hardcoded colors/fonts/sizes
- [ ] CC used via Claude Code CLI (mandatory)

## Mandatory Rules
- **CC CLI MANDATORY:** `cat brief.md | claude --model sonnet --print --dangerously-skip-permissions`
- CC env vars: `CLAUDE_CODE_ENTRYPOINT=ado-pipeline CLAUDE_CODE_DISABLE_AUTO_MEMORY=1 CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1 CLAUDE_CODE_GLOB_TIMEOUT_SECONDS=30`
- Work dir: `~/projects/fip/fait-v2/`
- Commit: `feat(fait-v2#2865): Design Agent service layer and UI components`
- No hardcoded colors/sizes/fonts — CSS variables only (`--color-primary`, `--color-accent-bg`, etc.)
- No Cognito, Entra-only
- Dockerfile.debian only
- varchar(36) for all GUID columns
- GuidFormat=None on ALL MySQL connections
- No `@{ var x = ... }` inside Razor `@if/@else` blocks with markup — use `@code` properties
- MudBlazor: do NOT use icon variants like `Icons.Material.Outlined.OutboxRounded` — use base icons only

## ADO Work Item Updates (MANDATORY — post as Fred White via mcporter)
After BUILD: `mcporter call devops.add_comment --args '{"project":"Fortress","id":2865,"text":"**[Tony Stark — BUILD cycle 1]**\nCommit {hash}: {summary}. Build: SUCCEEDED."}'`

## Deliverables
1. Build Report at `~/projects/fip/fait-v2/pipeline/ADO2865-BUILD-REPORT.md`
2. All changes committed and pushed to `origin/main`
3. ADO WI #2865 comment added with commit hash
