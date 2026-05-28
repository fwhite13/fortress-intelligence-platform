# ADO4559 Review Brief — WebFetchClient + MCP web_fetch endpoint + prompt update

You are performing an adversarial code review of ADO4559. Read each file listed, then answer each specific question below. Be precise and skeptical. Do not take code comments or variable names at face value — read the actual logic.

---

## Files to Read

1. `/home/fredw/projects/fip/fait/src/FortressAI.Web/Services/WebFetchClient.cs`
2. `/home/fredw/projects/fip/fait/src/FortressAI.Web/Program.cs` (focus: lines 300-345 and lines 825-910)
3. `/home/fredw/projects/fip/fait/src/FortressAI.Web/Services/DatabaseInitializationService.cs` (focus: lines 533-760)
4. `/home/fredw/projects/fip/fait/agent-harness/harness-server.js` (focus: lines 630-660, 755-835, 2005-2060, 4495-4535)
5. `/home/fredw/projects/fip/fait/agent-harness/CLAUDE.md`
6. `/home/fredw/projects/fip/fait/src/FortressAI.Web/FortressAI.Web.csproj`

---

## Specific Questions to Answer

### Q1: Redirect Limit (CRITICAL — already flagged by Tony)

In `WebFetchClient.cs`, a `HttpClientHandler` is created locally with `MaxAutomaticRedirections=3`. But the actual HTTP client is obtained via `_httpClientFactory.CreateClient("WebFetch")`. 

**Question:** Is the local `handler` variable (with MaxAutomaticRedirections=3) ever actually passed to the `IHttpClientFactory`-created client? Or is it dead code? When `CreateClient("WebFetch")` is called, it uses the factory's registered handler, not the local one. Is the "WebFetch" named client registered with a custom handler in Program.cs that enforces 3 redirects, or does it use a default handler (typically 50 redirects)?

Expected finding: The handler variable is dead code. The factory uses its own default handler. Redirect limit is NOT enforced.

### Q2: 2MB Truncation Implementation

In `WebFetchClient.cs`, examine the read loop carefully:

```csharp
var buffer = new byte[MaxResponseBytes + 1];
var bytesRead = 0;
int read;
var truncated = false;
while ((read = await stream.ReadAsync(buffer.AsMemory(bytesRead, Math.Min(4096, buffer.Length - bytesRead)), cts.Token)) > 0)
{
    bytesRead += read;
    if (bytesRead >= MaxResponseBytes)
    {
        truncated = true;
        break;
    }
}

var rawHtml = Encoding.UTF8.GetString(buffer, 0, Math.Min(bytesRead, MaxResponseBytes));
```

**Questions:**
- Is truncation actually enforced? Does it stop reading at 2MB?
- Is the buffer correctly sized? (Buffer is MaxResponseBytes+1 = 2MB+1 byte — why the +1, and does it matter?)
- When `bytesRead` hits 2MB+1 via the ReadAsync, does `Math.Min(4096, buffer.Length - bytesRead)` prevent a buffer overrun? (buffer.Length = 2MB+1, bytesRead could be up to 2MB+1, so buffer.Length - bytesRead could be 0 or negative — check if this is safe)
- Does the final `Encoding.UTF8.GetString(buffer, 0, Math.Min(bytesRead, MaxResponseBytes))` correctly cap at 2MB even if bytesRead > 2MB?

### Q3: JS Heuristic Logic

In `WebFetchClient.cs`:
```csharp
var isJsRendered = isHtml && markdown.Trim().Length < JsRenderThreshold;
```
where `JsRenderThreshold = 200`.

**Question:** Is this checking the right thing? The spec says "extracted text < 200 chars → IsJsRendered=true". After noise stripping (removing script/style/nav/footer/aside/header/form), is `markdown` the cleaned extracted text? What if a page has a very short real article — would this incorrectly flag it as JS-rendered? Is `isHtml` correctly determined from Content-Type before the body is read (before JS stripping)?

### Q4: Endpoint Security Check

In Program.cs, the `/internal/mcp/webfetch` endpoint:
- Is `IsInternalAuthorized` called before any processing?
- Does `IsInternalAuthorized` check `X-Internal-Token` against `INTERNAL_API_TOKEN` from config?
- Is the endpoint decorated with `.AllowAnonymous().DisableAntiforgery()` (matching the Brave pattern)?
- Could a request bypass the `IsInternalAuthorized` check and reach `webFetchClient.FetchAsync`?

### Q5: Harness Endpoint URL Match

In `harness-server.js` at line ~2014:
```javascript
const webFetchUrl = `${FAIT_BASE_URL}/internal/mcp/webfetch`;
```

**Question:** Does this URL path exactly match the `MapPost` path in Program.cs? Confirm it's `/internal/mcp/webfetch` in both places.

### Q6: Harness Agentic Loop Dispatch

In `harness-server.js` agentic loop (~line 4507):
- When `toolUseAccumulator.name === 'web_fetch'`, does the code call `http://localhost:{PORT}/tools/web_fetch`?
- Does `/tools/web_fetch` (the express handler) then forward to `${FAIT_BASE_URL}/internal/mcp/webfetch`?
- Is the MCP JSON-RPC envelope (`{jsonrpc, id, method: "tools/call", params: {name: "web_fetch", arguments: {url}}}`) correctly formed?
- Does the `userId` get forwarded in the body even though the webfetch endpoint doesn't use it? Is that harmless?

### Q7: DatabaseInitializationService — Seed Collision Check

The v2 migration inserts a row with hardcoded `id = "00000000-0000-0000-0000-000000000004"` and `slug = 'webfetch'`.

**Questions:**
- Is the `mcp-server-seed-v2` migration key different from `mcp-server-seed-v1`? (Confirm no collision)
- Does the v1 seed already use `00000000-0000-0000-0000-000000000004` as an ID? If so, the ON DUPLICATE KEY UPDATE clause would silently update the existing row.
- Is the INSERT correctly using `ON DUPLICATE KEY UPDATE` to be idempotent even if run outside the migration guard?
- After the INSERT, does the code correctly record the `mcp-server-seed-v2` migration key in `applied_migrations`?

### Q8: BraveSearchClient Untouched Verification

**Question:** Does the commit `dc84ac97` touch `BraveSearchClient.cs` at all? Run: `git -C /home/fredw/projects/fip/fait show dc84ac97 --stat` to verify.

### Q9: Markdown Conversion Coverage

In `WebFetchClient.cs`, does `ConvertNodeToMarkdown` handle all of these:
- h1-h6 → # through ######  ✓/✗
- p → paragraph ✓/✗
- ul/ol → list items ✓/✗
- a href → [text](href) ✓/✗
- table → markdown table ✓/✗
- strong/b → **bold** ✓/✗
- em/i → *italic* ✓/✗
- code → `code` ✓/✗
- pre → ```code block``` ✓/✗

### Q10: WebFetchClient Registration — Singleton with IHttpClientFactory

The spec requires registering as singleton. In Program.cs line 306:
```csharp
builder.Services.AddSingleton<IWebFetchClient, WebFetchClient>();
```

**Questions:**
- Is there a separate `AddHttpClient("WebFetch", ...)` registration? Or only the generic `AddHttpClient()` at line 105?
- If there's no named "WebFetch" client registration, what happens when `CreateClient("WebFetch")` is called — does it fall back to a default client?
- Is there a captive dependency issue? `WebFetchClient` is singleton but depends on `IHttpClientFactory`, which is also singleton — is this safe?

---

## Verdict Criteria

**FAIL** if:
- The redirect limit bug is present AND it's not clearly flagged as known/acceptable deviation
- Any of the endpoint security checks fail (webfetch accessible without token)
- DB seed ID collision with v1 seed

**NEEDS-CHANGES** if:
- Redirect limit bug confirmed (Tony already flagged this — this IS expected to be a NEEDS-CHANGES item)
- Any logic bugs in 2MB truncation (buffer overrun risk)
- Harness URL mismatch

**PASS** if:
- Only the redirect limit issue exists (already flagged by Tony as known deviation, fix prescribed)
- All other checks pass

Report each finding with: file, approximate line number, severity, description, and suggested fix.
