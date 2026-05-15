# CC Brief: ADO3351 — FAIT Harness: M365/ADO token-expiry logging + re-auth prompt

## Context

Working in repo: `/home/fredw/projects/fip/fait-v2`

Key files:
- `/home/fredw/projects/fip/fait-v2/src/FortressAI.V2.Web/Services/MicrosoftTokenService.cs`
- `/home/fredw/projects/fip/fait-v2/src/FortressAI.V2.Web/Services/IUserAgentRuntime.cs`
- `/home/fredw/projects/fip/fait-v2/src/FortressAI.V2.Web/Components/Chat/ChatView.razor`
- `/home/fredw/projects/fip/fait-v2/agent-harness/harness-server.js`

---

## Part 1: Blazor — Token-expiry detection logging in MicrosoftTokenService

File: `src/FortressAI.V2.Web/Services/MicrosoftTokenService.cs`

The current `GetValidAccessTokenAsync(string entraOid)` method has some logging but doesn't clearly distinguish all three failure modes. We need structured logging that distinguishes:

1. **Token missing** — no record in DB for this user (`token == null`)
2. **Token expired** — token exists but `ExpiresAt <= UtcNow` AND we cannot refresh (no refresh token or not configured)
3. **Token fetch failure** — exception during network/service call to refresh

### Changes required in MicrosoftTokenService.cs:

**For "token missing" case** (currently: `_logger.LogWarning("No Microsoft token found for entraOid={EntraOid}", entraOid)`):
- Change to include `tokenStatus` structured property:
```csharp
_logger.LogWarning("M365 token missing for user {UserId} — no token record in database. User must re-authorize at Settings → Integrations", entraOid);
```

Wait — actually the parameter name should stay as `EntraOid` since that's the identifier used here. But add a clear `tokenStatus` property:
```csharp
_logger.LogWarning("M365 token status: missing — no token record in database for {UserId}. User must re-authorize.", entraOid);
```

**For "token expired" case** — when `token.ExpiresAt <= UtcNow + 5min` AND we can't refresh (either `!IsConfigured || string.IsNullOrEmpty(token.RefreshToken)`):
- Currently just: `_logger.LogWarning("Cannot refresh token for {EntraOid} — missing config or refresh token", entraOid)`
- This needs to distinguish "token expired, no refresh token available" from "service not configured"
- Change to:
```csharp
if (!IsConfigured)
{
    _logger.LogWarning("M365 token status: expired — Azure client not configured, cannot refresh for {UserId}", entraOid);
}
else
{
    _logger.LogWarning("M365 token status: expired — no refresh token available for {UserId}. User must re-authorize at Settings → Integrations", entraOid);
}
return null;
```

**For "token fetch failure" case** — in the catch block:
- Currently: `_logger.LogError(ex, "Failed to refresh token for entraOid={EntraOid}", entraOid)`
- Change to:
```csharp
_logger.LogError(ex, "M365 token status: fetch-failure — exception refreshing token for {UserId}", entraOid);
```

Also update the log in the inner failure case (`!response.IsSuccessStatusCode`):
```csharp
_logger.LogError("M365 token status: fetch-failure — refresh HTTP {StatusCode} for {UserId}: {Body}", response.StatusCode, entraOid, body);
```

### IMPORTANT: The return type of GetValidAccessTokenAsync should also expose WHY it returned null

Instead of returning `string?` (null for all failure cases), we need to be able to communicate the failure reason to callers — specifically to the internal `/api/internal/user-tokens/{userId}` endpoint in `Program.cs` so it can include token status metadata in the response.

Change `IMicrosoftTokenService` interface:
- Add a new method: `Task<(string? Token, string TokenStatus)> GetTokenWithStatusAsync(string entraOid)` 
  - `TokenStatus` values: `"ok"`, `"missing"`, `"expired"`, `"fetch-failure"`
- Keep `GetValidAccessTokenAsync` as-is (for backward compat — it just calls the new method internally)

Implement `GetTokenWithStatusAsync` in `MicrosoftTokenService`:
- Token missing → return `(null, "missing")`
- Token exists, expired, no refresh → return `(null, "expired")`
- Token fetch failure → return `(null, "fetch-failure")`  
- Token valid/refreshed → return `(token, "ok")`

Update the existing `GetValidAccessTokenAsync` to delegate to `GetTokenWithStatusAsync`:
```csharp
public async Task<string?> GetValidAccessTokenAsync(string entraOid)
{
    var (token, _) = await GetTokenWithStatusAsync(entraOid);
    return token;
}
```

---

## Part 2: Harness — Token-expiry detection in getUserTokens + re-auth SSE event

File: `agent-harness/harness-server.js`

### 2a: Update /api/internal/user-tokens endpoint (Program.cs)

File: `src/FortressAI.V2.Web/Program.cs`

The `/api/internal/user-tokens/{userId}` endpoint currently returns:
```json
{ "ms365AccessToken": "...", "adoPersonalAccessToken": "..." }
```

Update it to also include token status fields using the new `GetTokenWithStatusAsync`:
```json
{
  "ms365AccessToken": null,
  "ms365TokenStatus": "expired",
  "adoPersonalAccessToken": "...",
  "adoTokenStatus": "ok"
}
```

For ADO: ADO PATs don't have the same expiry tracking (no expiry date in DB). For the ADO token status:
- If `GetDecryptedPatAsync` returns null → status is `"missing"`
- If it returns a value → status is `"ok"` (ADO PATs don't have server-side expiry tracking currently)

In the endpoint, change from:
```csharp
ms365Token = await microsoftTokenService.GetValidAccessTokenAsync(userId);
```
To:
```csharp
string? ms365Token = null;
string ms365TokenStatus = "ok";
try
{
    var (t, status) = await microsoftTokenService.GetTokenWithStatusAsync(userId);
    ms365Token = t;
    ms365TokenStatus = status;
}
catch (Exception ex) { ... ms365TokenStatus = "fetch-failure"; }

string? adoToken = null;
string adoTokenStatus = "ok";
try
{
    adoToken = await devOpsService.GetDecryptedPatAsync(userId);
    adoTokenStatus = adoToken != null ? "ok" : "missing";
}
catch ...
```

Return:
```csharp
return Results.Ok(new
{
    ms365AccessToken = ms365Token,
    ms365TokenStatus = ms365TokenStatus,
    adoPersonalAccessToken = adoToken,
    adoTokenStatus = adoTokenStatus
});
```

### 2b: Update getUserTokens() in harness-server.js

The `getUserTokens` function currently returns `{ ms365: token | null, ado: token | null }`.

Update it to also return status fields:
```javascript
return {
    ms365: data.ms365AccessToken ?? null,
    ms365Status: data.ms365TokenStatus ?? 'ok',
    ado: data.adoPersonalAccessToken ?? null,
    adoStatus: data.adoTokenStatus ?? 'ok'
};
```

And in the error/fallback cases, return:
```javascript
return { ms365: null, ms365Status: 'unknown', ado: null, adoStatus: 'unknown' };
```

### 2c: Detect expired token before dispatching graph_ or ado_ tools

In the tool dispatch section of harness-server.js (around line 2490–2530), before making the tool call:

**For graph_ tools** — before the `fetch` call to `/tools/graph_*`:
```javascript
// Check if MS365 token is available; if expired/missing, return re-auth error instead of calling tool
if (!userTokens.ms365) {
    const isExpired = userTokens.ms365Status === 'expired' || userTokens.ms365Status === 'fetch-failure';
    const statusMsg = isExpired
        ? 'Your Microsoft 365 authorization has expired. Please re-authorize at Settings → Integrations to continue using M365 tools.'
        : 'Microsoft 365 is not connected. Please connect your account at Settings → Integrations.';
    toolResultText = statusMsg;
    isError = true;
    emitToolCall(res, 'graph', toolUseAccumulator.name, 'error', 'M365 token unavailable');
    // Emit re-auth SSE event
    sendEvent({ type: 'reauth_required', provider: 'ms365', message: statusMsg });
    // Skip the fetch call
    // (set toolResultText and isError and continue without the try/catch below)
} else {
    // existing try/catch fetch code
}
```

**For ado_ tools** — similarly:
```javascript
if (!userTokens.ado) {
    const isExpired = userTokens.adoStatus === 'expired' || userTokens.adoStatus === 'fetch-failure';
    const statusMsg = isExpired
        ? 'Your Azure DevOps authorization has expired. Please re-connect your PAT at Settings → Integrations.'
        : 'Azure DevOps is not connected. Please connect your PAT at Settings → Integrations.';
    toolResultText = statusMsg;
    isError = true;
    emitToolCall(res, 'ado', toolUseAccumulator.name, 'error', 'ADO token unavailable');
    sendEvent({ type: 'reauth_required', provider: 'ado', message: statusMsg });
} else {
    // existing try/catch fetch code
}
```

---

## Part 3: Blazor ChatView — Handle reauth_required SSE event

File: `src/FortressAI.V2.Web/Components/Chat/ChatView.razor`

### 3a: Add `ReauthRequired` field to HarnessEvent

File: `src/FortressAI.V2.Web/Services/IUserAgentRuntime.cs`

Add to the `HarnessEvent` record:
```csharp
public record HarnessEvent(
    string Type,
    string? Content = null,
    int? ExitCode = null,
    string? ErrorMessage = null,
    int? InputTokens = null,
    int? OutputTokens = null,
    string? Provider = null,       // for reauth_required events: "ms365" | "ado"
    string? Message = null         // for reauth_required events: the user-facing message
);
```

### 3b: Handle the event in ChatView.razor

In `RunStreamingTurnAsync`, in the `await foreach (var evt in AgentRuntime.SendTurnAsync(...))` loop, add handling for `reauth_required` event type:

```csharp
else if (evt.Type == "reauth_required")
{
    var provider = evt.Provider ?? "ms365";
    var providerLabel = provider == "ado" ? "Azure DevOps" : "Microsoft 365";
    var reAuthMessage = evt.Message ?? $"Your {providerLabel} authorization has expired. Please re-authorize at Settings → Integrations to continue.";
    // Show as a styled assistant message with a link to /connectors
    _messages.Add(new ChatMessage(
        Role: "assistant",
        Content: $"🔐 **{providerLabel} Re-authorization Required**\n\n{reAuthMessage}\n\n[Go to Settings → Integrations](/connectors)"
    ));
    StateHasChanged();
    // Don't break — the stream may continue with more events
}
```

Note: The ChatView renders messages using markdown (check if it does — if it uses MarkupString or plain text). Look at how `_messages` are rendered in the razor markup to determine if markdown links work or if we need HTML. If plain text, adjust accordingly.

Check the ChatView markup section to see how assistant messages are rendered (look for `ChatMessage` display code). If messages use a markdown renderer, use the markdown link format. If they use raw HTML/MarkupString, use `<a href="/connectors">Settings → Integrations</a>`. If plain text, just include the URL: `/connectors`.

---

## Execution Order

1. First read and understand the current code before making any changes
2. Make all changes using the edit tool (not write, since we're modifying existing files)
3. Part 1: MicrosoftTokenService.cs changes
4. Part 1b: IUserAgentRuntime.cs - add Provider and Message to HarnessEvent
5. Part 2a: Program.cs - update /api/internal/user-tokens endpoint
6. Part 2b + 2c: harness-server.js - getUserTokens returns status fields + pre-dispatch token check
7. Part 3: ChatView.razor - handle reauth_required event
8. Commit all changes to git (one commit per part, or one combined commit)

---

## Constraints
- Do NOT change authentication flows — this is logging + UX messaging only
- Do NOT modify the token refresh logic, encryption, or storage
- Keep all existing behavior; only ADD new logging, status fields, and the re-auth prompt
- The re-auth check in harness should NOT break existing behavior when tokens are present
- Commit to origin/main when done
