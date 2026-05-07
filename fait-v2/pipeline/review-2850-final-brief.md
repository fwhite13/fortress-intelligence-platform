# CC Adversarial Review Brief — ADO#2850 (Final Verification Pass)
## Reviewer: Hawkeye (Clint Barton)

You are confirming specific pre-identified issues and verifying remaining checks on FAIT v2 ADO#2850.
Source files have already been read. Below is the extracted content for each file.

---

## FILE 1: ChatView.razor (full content)

```razor
@using FortressAI.V2.Web.Models
@using FortressAI.V2.Web.Services
@using Microsoft.AspNetCore.Components.Authorization

<div class="chat-container">
    <div class="chat-messages" @ref="_messagesRef">
        @if (!_messages.Any())
        {
            <div class="chat-empty-state">
                <MudIcon Icon="@Icons.Material.Filled.Chat" Class="chat-empty-icon" />
                <p class="chat-empty-title">What can I help you with?</p>
                <p class="chat-empty-subtitle">Ask anything, or start with one of your recent topics.</p>
            </div>
        }
        else
        {
            @foreach (var msg in _messages)
            {
                <MessageBubble Message="@msg" />
            }
            @if (_isStreaming)
            {
                <div class="chat-streaming-indicator">
                    <span class="chat-streaming-text">@_streamingContent</span>
                    <span class="chat-streaming-cursor">▋</span>
                </div>
            }
        }
    </div>

    <div class="chat-input-bar">
        <div class="chat-input-top-row">
            <div class="chat-kb-pills">
                <button @onclick="ToggleFortressKb" title="Search Fortress knowledge base" style="@GetFortressKbStyle()">
                    <MudIcon Icon="@Icons.Material.Filled.AccountBalance" Class="chat-pill-icon" />
                    <span class="chat-pill-label">Fortress KB</span>
                </button>
                <button @onclick="TogglePersonalKb" title="My personal knowledge base" style="@GetPersonalKbStyle()">
                    <MudIcon Icon="@Icons.Material.Filled.Person" Class="chat-pill-icon" />
                    <span class="chat-pill-label">My KB</span>
                </button>
            </div>
        </div>
        <div class="chat-input-bottom-row">
            <textarea class="chat-input-field"
                      @bind="_inputText"
                      @bind:event="oninput"
                      @onkeydown="HandleKeyDown"
                      placeholder="Message your assistant..."
                      rows="1"
                      disabled="@_isStreaming"></textarea>
            <button class="chat-send-btn"
                    @onclick="SendMessage"
                    disabled="@(string.IsNullOrWhiteSpace(_inputText) || _isStreaming)"
                    title="Send message">
                <MudIcon Icon="@Icons.Material.Filled.Send" Class="chat-send-icon" />
            </button>
        </div>
    </div>
</div>

@code {
    [Inject] private IUserAgentRuntime AgentRuntime { get; set; } = default!;
    [CascadingParameter] private Task<AuthenticationState>? AuthState { get; set; }

    private List<ChatMessage> _messages = new();
    private string _inputText = "";
    private bool _isStreaming = false;
    private string _streamingContent = "";
    private ElementReference _messagesRef;
    private string _userId = "";
    private bool _fortressKbEnabled = false;
    private bool _personalKbEnabled = true;

    protected override async Task OnInitializedAsync()
    {
        if (AuthState == null) return;
        var auth = await AuthState;
        _userId = auth.User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
               ?? auth.User.FindFirst("oid")?.Value ?? "";
    }

    private string GetFortressKbStyle() =>
        _fortressKbEnabled
            ? "display: flex; align-items: center; padding: 0 var(--space-3); height: 28px; border-radius: var(--radius-md); border: 1px solid var(--color-gold); background: var(--color-gold-muted); color: var(--color-gold); cursor: pointer;"
            : "display: flex; align-items: center; padding: 0 var(--space-3); height: 28px; border-radius: var(--radius-md); border: 1px solid var(--color-border); background: transparent; color: var(--color-text-secondary); cursor: pointer;";

    private string GetPersonalKbStyle() =>
        _personalKbEnabled
            ? "display: flex; align-items: center; padding: 0 var(--space-3); height: 28px; border-radius: var(--radius-md); border: 1px solid var(--color-gold); background: var(--color-gold-muted); color: var(--color-gold); cursor: pointer;"
            : "display: flex; align-items: center; padding: 0 var(--space-3); height: 28px; border-radius: var(--radius-md); border: 1px solid var(--color-border); background: transparent; color: var(--color-text-secondary); cursor: pointer;";

    private async Task SendMessage()
    {
        if (string.IsNullOrWhiteSpace(_inputText) || _isStreaming) return;

        var userMessage = _inputText.Trim();
        _inputText = "";
        _messages.Add(new ChatMessage(Role: "user", Content: userMessage));
        _isStreaming = true;
        _streamingContent = "";
        StateHasChanged();

        var assistantContent = new System.Text.StringBuilder();
        try
        {
            var request = new TurnRequest(
                Message: userMessage,
                SystemPrompt: BuildSystemPrompt()
            );
            await foreach (var evt in AgentRuntime.SendTurnAsync(_userId, request))
            {
                if (evt.Type == "text" && evt.Content != null)
                {
                    assistantContent.Append(evt.Content);
                    _streamingContent = assistantContent.ToString();
                    StateHasChanged();
                }
                else if (evt.Type == "done" || evt.Type == "error")
                {
                    break;
                }
            }
        }
        finally
        {
            _isStreaming = false;
            if (assistantContent.Length > 0)
                _messages.Add(new ChatMessage(Role: "assistant", Content: assistantContent.ToString()));
            _streamingContent = "";
            StateHasChanged();
        }
    }

    private string BuildSystemPrompt()
    {
        var parts = new List<string>();
        if (_fortressKbEnabled) parts.Add("Search the Fortress knowledge base for relevant context.");
        if (_personalKbEnabled) parts.Add("Search the user's personal knowledge base for relevant context.");
        return string.Join(" ", parts);
    }

    private async Task HandleKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter" && !e.ShiftKey)
            await SendMessage();
    }

    private void ToggleFortressKb() => _fortressKbEnabled = !_fortressKbEnabled;
    private void TogglePersonalKb() => _personalKbEnabled = !_personalKbEnabled;
}
```

---

## FILE 2: MessageBubble.razor (full content)

```razor
@using FortressAI.V2.Web.Models

<div class="message-bubble @(Message.Role == "user" ? "message-user" : "message-assistant")">
    <div class="message-content">@((MarkupString)RenderMarkdown(Message.Content))</div>
</div>

@code {
    [Parameter, EditorRequired] public ChatMessage Message { get; set; } = default!;

    private string RenderMarkdown(string content)
    {
        // TODO: wire Markdig in Sprint 3
        return System.Net.WebUtility.HtmlEncode(content)
            .Replace("\n", "<br/>");
    }
}
```

---

## FILE 3: ChatMessage.cs (full content)

```csharp
namespace FortressAI.V2.Web.Models;

public record ChatMessage(string Role, string Content, DateTimeOffset? Timestamp = null);
```

---

## FILE 4: Dashboard.razor (full content)

```razor
@page "/"
@attribute [Authorize]
@using FortressAI.V2.Web.Components.Layout
@using FortressAI.V2.Web.Components.Chat

<PageTitle>Dashboard — FAIT v2</PageTitle>

<DualPaneLayout @bind-IsPanelOpen="_previewOpen"
                PreviewTitle="@_previewTitle">
    <ChatContent>
        <ChatView />
    </ChatContent>
    <PreviewContent>
        <div class="dual-pane-preview-empty">
            No artifact selected.
        </div>
    </PreviewContent>
</DualPaneLayout>

@code {
    private bool _previewOpen = false;
    private string _previewTitle = "Preview";
}
```

---

## FILE 5: IUserAgentRuntime.cs (TurnRequest + HarnessEvent)

```csharp
public record TurnRequest(
    string Message,
    string? SystemPrompt = null,
    string? SessionId = null
);

public record HarnessEvent(
    string Type,         // "text" | "log" | "done" | "error"
    string? Content = null,
    int? ExitCode = null,
    string? ErrorMessage = null
);
```

---

## FILE 6: app.css — ADO#2850 block (lines 472-650 approx)

```css
.chat-container {
    display: flex;
    flex-direction: column;
    height: 100%;
    background: var(--color-bg-page);
}

.chat-messages {
    flex: 1;
    overflow-y: auto;
    padding: var(--space-4);
    display: flex;
    flex-direction: column;
    gap: var(--space-3);
}

.chat-empty-state {
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    flex: 1;
    text-align: center;
    padding: var(--space-8) var(--space-4);
    color: var(--color-text-secondary);
}

.chat-empty-icon {
    font-size: var(--text-3xl) !important;
    opacity: 0.3;
    margin-bottom: var(--space-3);
}

.chat-empty-title {
    font-size: var(--text-lg);
    font-weight: var(--font-semibold);
    color: var(--color-text-primary);
    margin: 0 0 var(--space-1);
}

.chat-empty-subtitle {
    font-size: var(--text-sm);
    color: var(--color-text-secondary);
    margin: 0;
}

.message-bubble {
    max-width: 80%;
    padding: var(--space-3) var(--space-4);
    border-radius: var(--radius-lg);
    line-height: 1.6;
    font-size: var(--text-base);
}

.message-user {
    align-self: flex-end;
    background: var(--color-primary);
    color: var(--color-text-on-primary);
    border-bottom-right-radius: var(--radius-sm);
}

.message-assistant {
    align-self: flex-start;
    background: var(--color-surface);
    color: var(--color-text-primary);
    border: 1px solid var(--color-border);
    border-bottom-left-radius: var(--radius-sm);
}

.message-content {
    white-space: pre-wrap;
    word-break: break-word;
}

.chat-streaming-indicator {
    align-self: flex-start;
    background: var(--color-surface);
    border: 1px solid var(--color-border);
    border-radius: var(--radius-lg);
    border-bottom-left-radius: var(--radius-sm);
    padding: var(--space-3) var(--space-4);
    font-size: var(--text-base);
    color: var(--color-text-primary);
    max-width: 80%;
}

.chat-streaming-cursor {
    animation: blink 1s step-end infinite;
}

@keyframes blink {
    50% { opacity: 0; }
}

.chat-input-bar {
    border-top: 1px solid var(--color-border);
    background: var(--color-bg-page);
    padding: var(--space-2) var(--space-4) var(--space-3);
    flex-shrink: 0;
}

.chat-input-top-row {
    display: flex;
    align-items: center;
    margin-bottom: var(--space-2);
}

.chat-kb-pills {
    display: flex;
    gap: var(--space-2);
}

.chat-pill-icon {
    font-size: 16px !important;
    width: 16px;
    height: 16px;
}

.chat-pill-label {
    font-size: var(--text-sm);
    font-weight: var(--font-medium);
    margin-left: var(--space-1);
}

.chat-input-bottom-row {
    display: flex;
    gap: var(--space-2);
    align-items: flex-end;
}

.chat-input-field {
    flex: 1;
    border: 1px solid var(--color-border);
    border-radius: var(--radius-md);
    padding: var(--space-2) var(--space-3);
    font-family: var(--font-primary);
    font-size: var(--text-base);
    color: var(--color-text-primary);
    background: var(--color-surface);
    resize: none;
    outline: none;
    transition: border-color var(--transition-fast);
    min-height: 44px;
    max-height: 200px;        /* <-- LINE 618: HARDCODED */
}

.chat-input-field:focus {
    border-color: var(--color-primary);
}

.chat-send-btn {
    background: var(--color-primary);
    color: var(--color-text-on-primary);
    border: none;
    border-radius: var(--radius-md);
    width: 44px;
    height: 44px;
    display: flex;
    align-items: center;
    justify-content: center;
    cursor: pointer;
    flex-shrink: 0;
    transition: opacity var(--transition-fast);
}

.chat-send-btn:disabled {
    opacity: 0.4;
    cursor: not-allowed;
}

.chat-send-icon {
    font-size: 20px !important;
}
```

---

## Verification Tasks

### TASK 1 — Confirm C7 (Critical): max-height: 200px
Look at `.chat-input-field` in the CSS above.
- Is `max-height: 200px` hardcoded on line 618? YES/NO
- Is 200px in the approved exception list (28px, 44px, 80%, 1.6, 20px, 16px)? YES/NO
- Verdict: CONFIRMED ISSUE or FALSE POSITIVE

### TASK 2 — Confirm I2 (Important): BuildSystemPrompt returns "" not null
Look at `BuildSystemPrompt()` in ChatView.razor.
- When both `_fortressKbEnabled = false` and `_personalKbEnabled = false`, what does `string.Join(" ", parts)` return when `parts` is empty?
- `TurnRequest.SystemPrompt` is `string?` (nullable) — what is the semantic difference between passing `""` vs `null`?
- Verdict: CONFIRMED ISSUE or FALSE POSITIVE

### TASK 3 — Check build: 0 errors/warnings
Look at all C# code carefully.
- Is `_messagesRef` (ElementReference) declared but never explicitly used in code (JS interop deferred)?
- In Blazor, does `@ref="_messagesRef"` on a DOM element generate a compiler warning for unused field? (Answer: No, because Blazor's source generator emits assignment; it is used by the framework)
- Are there any obvious syntax errors in ChatView.razor or MessageBubble.razor?
- Verdict: BUILD CLEAN or BUILD ISSUES

### TASK 4 — KB pill active state uses var(--color-gold)
Look at `GetFortressKbStyle()` and `GetPersonalKbStyle()` in ChatView.razor.
- Active state: does it use `var(--color-gold)` for border and color? YES/NO
- Active state: does it use `var(--color-gold-muted)` for background? YES/NO
- Does it use `var(--accent-blue)` or any hex color? YES/NO
- Verdict: PASS or FAIL

### TASK 5 — SendMessage calls AgentRuntime.SendTurnAsync and streams correctly
Look at `SendMessage()` in ChatView.razor.
- Does it call `AgentRuntime.SendTurnAsync(_userId, request)`? YES/NO
- Does it `await foreach` over result? YES/NO
- Does it check `evt.Type == "text"` and accumulate content? YES/NO
- Does it call `StateHasChanged()` per token? YES/NO
- Does it break on "done" or "error"? YES/NO
- Verdict: PASS or FAIL

### TASK 6 — _userId from objectidentifier claim
Look at `OnInitializedAsync()` in ChatView.razor.
- Does it read from `"http://schemas.microsoft.com/identity/claims/objectidentifier"`? YES/NO
- Does it have `"oid"` fallback? YES/NO
- Is it NOT from name/email/UPN? YES/NO
- Verdict: PASS or FAIL

### TASK 7 — RenderMarkdown XSS safe
Look at `RenderMarkdown()` in MessageBubble.razor.
- Does it call `HtmlEncode` FIRST, then `.Replace("\n", "<br/>")`? YES/NO
- If reversed (Replace first, then Encode), `<br/>` would be encoded as `&lt;br/&gt;` and show as literal text. Is it the correct order? YES/NO
- Verdict: PASS or FAIL

### TASK 8 — Dashboard.razor has ChatView inside ChatContent of DualPaneLayout
Look at Dashboard.razor.
- Is `<ChatView />` inside `<ChatContent>` slot? YES/NO
- Is it inside `DualPaneLayout`? YES/NO
- Verdict: PASS or FAIL

---

## Expected output format

For each task:
```
[TASK N] Title: CONFIRMED/PASS/FAIL — one-line finding
```

Then summary:
```
C7 CONFIRMED: yes/no
I2 CONFIRMED: yes/no
All other checks: PASS/FAIL list
New issues found: none / [description]
```
