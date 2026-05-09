# Build Brief: ADO#3122 + ADO#3119

Working directory: /home/fredw/projects/fip/fait-v2

## TASK 1 — ADO#3122: Chat UI full v1 visual parity

### Context
V1 reference has been analyzed. Here is exactly what must change in v2 to match v1.

### File 1: `src/FortressAI.V2.Web/Components/Chat/MessageBubble.razor`

Current state: The component uses a flat `message-bubble` class with no avatar, no message-body structure. Content is `(MarkupString)RenderMarkdown(Message.Content)` — but RenderMarkdown currently does HTML encode + line breaks (no Markdig).

**Required changes:**

1. **Add Markdig** — wire proper Markdig markdown rendering (same as v1's `RenderMarkdown` using `Markdig.Markdown.ToHtml`). V1 uses `@using Markdig` and `Markdown.ToHtml(content, Pipeline)` with `MarkdownPipelineBuilder().UseAdvancedExtensions().Build()`.

2. **Add avatar + message-body structure** — Match v1's layout:
   - Outer: `<div class="message message-user" or "message message-assistant">`
   - Inner left: `<div class="message-avatar">` with a user initial avatar (gold circle, user initial letter) for user, or a Shield icon for assistant
   - Inner right: `<div class="message-body"><div class="message-content">...</div></div>`
   - For user: content is plain text `<p>@Message.Content</p>` 
   - For assistant: content is `@((MarkupString)RenderMarkdown(Message.Content))` + optional streaming cursor
   - Token display: below message-body, show `<div class="message-meta">` with token-count for assistant messages where TokensIn > 0 || TokensOut > 0
   - Replace all uses of `message-bubble` CSS class with the v1-style `message` + `message-user`/`message-assistant` classes

3. **Add parameters**: The component needs `[Parameter] public string? UserInitial { get; set; }` and `[Parameter] public List<ChatAttachment> Attachments { get; set; } = new();`

Here is the exact replacement content for MessageBubble.razor:

```razor
@inject IJSRuntime JS
@using Markdig
@using FortressAI.V2.Web.Models

<div class="message @(Message.Role == "user" ? "message-user" : "message-assistant") @(IsStreaming ? "streaming" : "")">
    <div class="message-avatar">
        @if (Message.Role == "user")
        {
            @if (!string.IsNullOrEmpty(UserInitial))
            {
                <span style="display:inline-flex;align-items:center;justify-content:center;width:36px;height:36px;border-radius:50%;background:var(--color-primary);color:white;font-size:0.75rem;font-weight:700;letter-spacing:0.05em;flex-shrink:0;">
                    @UserInitial
                </span>
            }
            else
            {
                <MudIcon Icon="@Icons.Material.Filled.Person" Size="Size.Small" />
            }
        }
        else
        {
            <MudIcon Icon="@Icons.Material.Filled.Shield" Size="Size.Small" Style="color: var(--color-gold);" />
        }
    </div>
    <div class="message-body">
        <div class="message-content">
            @if (Message.Role == "assistant")
            {
                @((MarkupString)RenderMarkdown(Message.Content))
                @if (IsStreaming)
                {
                    <span class="cursor-blink">▊</span>
                }
            }
            else
            {
                <p>@Message.Content</p>
            }
        </div>
        @if (!IsStreaming && Message.Role == "assistant" && (Message.TokensIn > 0 || Message.TokensOut > 0))
        {
            <div class="message-meta">
                <span class="token-count">
                    <MudIcon Icon="@Icons.Material.Filled.Memory" Size="Size.Small" />
                    @(Message.TokensIn?.ToString("N0") ?? "?")→@(Message.TokensOut?.ToString("N0") ?? "?") tokens
                </span>
            </div>
        }
    </div>
</div>

@code {
    [Parameter, EditorRequired] public ChatMessage Message { get; set; } = default!;
    [Parameter] public bool IsStreaming { get; set; } = false;
    [Parameter] public string? UserInitial { get; set; }

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    private string RenderMarkdown(string? content)
    {
        if (string.IsNullOrEmpty(content)) return "";
        try
        {
            return Markdig.Markdown.ToHtml(content, Pipeline);
        }
        catch
        {
            return System.Net.WebUtility.HtmlEncode(content);
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        try
        {
            await JS.InvokeVoidAsync("fortressChat.highlightCode");
        }
        catch { }
    }
}
```

### File 2: `src/FortressAI.V2.Web/wwwroot/css/fortress.css`

Find the `.message-bubble` section (around line 2582) and replace the entire message-bubble block with v1-style classes. Also fix the `.message-user .message-content` background.

**Find and replace this section** (the message-bubble block at ~line 2582):

```css
/* Message bubbles — match FAIT v1 */
.message-bubble {
    display: flex;
    flex-direction: column;
    margin-bottom: var(--space-4);
    max-width: var(--chat-content-max-width);
    width: 100%;
    margin-left: auto;
    margin-right: auto;
}

.message-bubble .message-content {
    font-size: var(--text-base);
    line-height: 1.65;
}

.message-bubble.message-user .message-content {
    background: var(--color-primary);
    color: var(--color-text-on-primary);
    padding: var(--space-3) var(--space-4);
    border-radius: var(--radius-lg);
    border-top-right-radius: var(--radius-sm);
    align-self: flex-end;
    max-width: 80%;
}

.message-bubble.message-assistant .message-content {
    padding: var(--space-1) 0;
}

.message-bubble .message-meta {
    display: flex;
    align-items: center;
    gap: var(--space-3);
    margin-top: var(--space-2);
    font-size: var(--text-xs);
    color: var(--color-text-muted);
}
```

**Replace it with:**

```css
/* Message bubbles — FAIT v1 visual parity (ADO#3122) */
/* Uses .message / .message-user / .message-assistant / .message-avatar / .message-body / .message-content */
/* These classes already exist in the chat section above (~line 906) and are shared */
/* No duplicate rules needed — message-bubble class removed */
```

**Also find the existing `.message-user .message-content` rule** (~line 937) which currently has:

```css
.message-user .message-content {
  background: var(--color-primary-light);
  padding: 0.75rem 1rem;
  border-radius: var(--radius-lg);
  border-top-left-radius: var(--radius-sm);
}
```

This matches v1 already (color-primary-light = #EEF2F7 light blue-grey). Keep it as-is. The commit 19f68647 may have put `var(--color-primary)` here — if it did, revert it back to `var(--color-primary-light)`.

Check current value and if it says `var(--color-primary)` change it to `var(--color-primary-light)`.

### File 3: `src/FortressAI.V2.Web/Components/Chat/ChatView.razor`

Find where MessageBubble components are rendered and pass the UserInitial parameter. Look for:
```razor
<MessageBubble Message="@msg" IsStreaming="@(msg == _messages.LastOrDefault() && _isStreaming)" />
```

Change to:
```razor
<MessageBubble Message="@msg" IsStreaming="@(msg == _messages.LastOrDefault() && _isStreaming)" UserInitial="@_userInitial" />
```

Also need to make sure `_userInitial` is populated. Search for existing `_userInitial` field. If it doesn't exist, add it. Look for how `_userId` is set and add:
```csharp
private string _userInitial = "U";
```
Set it from user display name when the user context is loaded (search for where _userId is set).

### Microphone button placeholder

In `src/FortressAI.V2.Web/Components/Chat/ChatView.razor`, find the `chat-input-bottom-row` div which contains the textarea, Run as Task btn, and send btn. Add a mic button placeholder BETWEEN the textarea and the send button:

```razor
<button class="chat-mic-btn"
        type="button"
        disabled="@(_isStreaming || _ccRunning || !_harnessReady)"
        title="Voice input (coming soon)">
    <MudIcon Icon="@Icons.Material.Filled.Mic" Class="chat-send-icon" />
</button>
```

Add the CSS for `.chat-mic-btn` in fortress.css — style it like the send button but with `var(--color-surface)` background and `var(--color-text-secondary)` color:

```css
/* Mic button — voice input placeholder (ADO#3122) */
.chat-mic-btn {
    width: var(--space-10);
    height: var(--space-10);
    border: 1px solid var(--color-border);
    border-radius: var(--radius-md);
    background: var(--color-surface);
    color: var(--color-text-secondary);
    cursor: pointer;
    display: flex;
    align-items: center;
    justify-content: center;
    flex-shrink: 0;
    transition: background var(--transition-fast);
}

.chat-mic-btn:hover:not(:disabled) {
    background: var(--color-surface-sunken);
    color: var(--color-gold);
}

.chat-mic-btn:disabled {
    opacity: 0.4;
    cursor: not-allowed;
}
```

### Markdig dependency check

Check if Markdig is already in the v2 project:
```
grep -r "Markdig" /home/fredw/projects/fip/fait-v2/src/FortressAI.V2.Web/FortressAI.V2.Web.csproj
```

If not present, add it:
```
cd /home/fredw/projects/fip/fait-v2 && dotnet add src/FortressAI.V2.Web/FortressAI.V2.Web.csproj package Markdig
```

---

## TASK 2 — ADO#3119: Entra OID backfill middleware

### File: `src/FortressAI.V2.Web/Program.cs`

Find this exact line:
```csharp
app.UseAuthentication();
app.UseAuthorization();
```

Insert the following middleware BETWEEN `app.UseAuthentication();` and `app.UseAuthorization();`:

```csharp
// ADO#3119 — Backfill entra_oid for authenticated users (cookie consumer — no OIDC callback)
app.Use(async (context, next) =>
{
    try
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var oid = context.User.FindFirst("oid")?.Value
                   ?? context.User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;
            if (!string.IsNullOrEmpty(oid))
            {
                var dbFactory = context.RequestServices.GetRequiredService<IDbContextFactory<FaitV2DbContext>>();
                await using var db = await dbFactory.CreateDbContextAsync();
                var user = await db.Users.FirstOrDefaultAsync(u => u.EntraOid == oid);
                if (user == null)
                {
                    var email = context.User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
                             ?? context.User.FindFirst("preferred_username")?.Value;
                    if (!string.IsNullOrEmpty(email))
                    {
                        var staleUser = await db.Users.FirstOrDefaultAsync(
                            u => u.Email == email && (u.EntraOid == null || u.EntraOid == ""));
                        if (staleUser != null)
                        {
                            staleUser.EntraOid = oid;
                            staleUser.UpdatedAt = DateTime.UtcNow;
                            await db.SaveChangesAsync();
                            var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
                            logger.LogInformation("[ADO#3119] Backfilled entra_oid for user {UserId}", staleUser.Id);
                        }
                    }
                }
            }
        }
    }
    catch (Exception ex)
    {
        var logger = context.RequestServices.GetService<ILogger<Program>>();
        logger?.LogWarning(ex, "[ADO#3119] entra_oid backfill failed — continuing");
    }
    await next(context);
});
```

---

## After all changes

1. Run `dotnet build src/FortressAI.V2.Web/FortressAI.V2.Web.csproj` from `/home/fredw/projects/fip/fait-v2`
2. Fix any build errors
3. `git -C /home/fredw/projects/fip/fait-v2 add src/FortressAI.V2.Web/Components/Chat/MessageBubble.razor src/FortressAI.V2.Web/wwwroot/css/fortress.css src/FortressAI.V2.Web/Components/Chat/ChatView.razor src/FortressAI.V2.Web/Program.cs src/FortressAI.V2.Web/FortressAI.V2.Web.csproj`
4. `git -C /home/fredw/projects/fip/fait-v2 commit -m "fix(fait#3122,fait#3119): full chat UI v1 parity rebuild + entra_oid backfill middleware"`
5. Run mcporter commands to add ADO comments:
   - `mcporter call devops.add_comment project="Fortress" id=3122 text="Chat UI v1 visual parity: Switched MessageBubble to v1-style avatar+body layout with Markdig markdown rendering. User bubble uses color-primary-light background (matches v1). Assistant bubble has no background per v1. Token display format matches v1 (in→out tokens). Added mic button placeholder in input bar. All values use CSS variables."`  
   - `mcporter call devops.add_comment project="Fortress" id=3119 text="Middleware added after app.UseAuthentication() — backfills entra_oid for users with null OID by matching on email claim. Wrapped in try/catch, never blocks request. dotnet build 0 errors."`
6. Write build reports to `/home/fredw/projects/fip/fait-v2/pipeline/ADO3122-BUILD-REPORT.md` and `/home/fredw/projects/fip/fait-v2/pipeline/ADO3119-BUILD-REPORT.md`
