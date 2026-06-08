# ADO#4936 — Fix DOCX/XLSX Preview 403 (Cloudflare Access / Public URL)

## Root Cause
`DocxPreviewPanel.razor` and `XlsxPreviewPanel.razor` both:
1. Call `NavManager.ToAbsoluteUri(relativeUrl)` which produces the full public URL `https://fait.dev.fortressam.ai/api/artifacts/...`
2. Call `HttpClientFactory.CreateClient()` (unnamed/default, no BaseAddress)

This routes the server-side HTTP request out through Cloudflare Access, which rejects it with 403 because the server-side HttpClient has no CF Access token.

## Fix

### 1. `src/FortressAI.Web/Program.cs`

Find the block of named HttpClient registrations (around line 294–330). Add this entry **after** the existing `builder.Services.AddHttpClient();` default registration (line 79) or grouped with the named clients — put it right after the line `builder.Services.AddHttpClient();`:

```csharp
// Named HttpClient for internal server-side preview fetches — bypasses Cloudflare Access
builder.Services.AddHttpClient("InternalPreview", c => { c.BaseAddress = new Uri("http://localhost/"); });
```

Do NOT remove the existing `builder.Services.AddHttpClient();` at line 79. Just add the new named registration after it or near the other named HttpClient registrations (lines 294–328).

### 2. `src/FortressAI.Web/Components/Chat/DocxPreviewPanel.razor`

In the `FetchBytesAsync()` method, replace these two lines:
```csharp
var absoluteUrl = NavManager.ToAbsoluteUri(relativeUrl).ToString();
Console.WriteLine($"[DocxPreview] FetchBytesAsync: token generated, url={absoluteUrl}");
using var http = HttpClientFactory.CreateClient();
using var response = await http.GetAsync(absoluteUrl);
Console.WriteLine($"[DocxPreview] HTTP {(int)response.StatusCode} {response.StatusCode} for {absoluteUrl}");
```

With:
```csharp
Console.WriteLine($"[DocxPreview] FetchBytesAsync: token generated, url={relativeUrl}");
using var http = HttpClientFactory.CreateClient("InternalPreview");
using var response = await http.GetAsync(relativeUrl);
Console.WriteLine($"[DocxPreview] HTTP {(int)response.StatusCode} {response.StatusCode} for {relativeUrl}");
```

Also remove the `@inject NavigationManager NavManager` directive at the top since it's no longer used.

**IMPORTANT:** Do NOT touch the HTML markup, styling, or any other logic. Only change the `FetchBytesAsync()` method and the inject directive.

### 3. `src/FortressAI.Web/Components/Chat/XlsxPreviewPanel.razor`

In the `LoadAndRenderAsync()` method, replace these two lines:
```csharp
var absoluteUrl = NavManager.ToAbsoluteUri(relativeUrl).ToString();
Console.WriteLine($"[XlsxPreview] LoadAndRenderAsync: url={absoluteUrl}");
using var http = HttpClientFactory.CreateClient();
using var response = await http.GetAsync(absoluteUrl);
Console.WriteLine($"[XlsxPreview] HTTP {(int)response.StatusCode} {response.StatusCode} for {absoluteUrl}");
```

With:
```csharp
Console.WriteLine($"[XlsxPreview] LoadAndRenderAsync: url={relativeUrl}");
using var http = HttpClientFactory.CreateClient("InternalPreview");
using var response = await http.GetAsync(relativeUrl);
Console.WriteLine($"[XlsxPreview] HTTP {(int)response.StatusCode} {response.StatusCode} for {relativeUrl}");
```

Also remove the `@inject NavigationManager NavManager` directive at the top since it's no longer used.

**IMPORTANT:** Do NOT touch the HTML markup, styling, tabs logic, JS interop calls, or any other logic. Only change the `LoadAndRenderAsync()` method and the inject directive.

## Constraints
- Do NOT change any other files
- Do NOT change the HTML/CSS/markup in either razor file
- Do NOT change the `@inject` directives other than removing `NavigationManager NavManager` from each component
- Do NOT change the `PreviewSvc.GenerateToken()` call or the `relativeUrl` construction — those stay exactly as-is
- The relative URL pattern stays: `$"/api/artifacts/{Artifact.Id}/preview?token=...&expires=..."`
- Keep all existing Console.WriteLine log statements (just update them to log `relativeUrl` instead of `absoluteUrl`)

## Files to Modify
1. `/home/fredw/projects/fip/fait/src/FortressAI.Web/Program.cs`
2. `/home/fredw/projects/fip/fait/src/FortressAI.Web/Components/Chat/DocxPreviewPanel.razor`
3. `/home/fredw/projects/fip/fait/src/FortressAI.Web/Components/Chat/XlsxPreviewPanel.razor`

## Acceptance Criteria
- `HttpClientFactory.CreateClient("InternalPreview")` is used in both panel components
- `NavManager.ToAbsoluteUri()` is NOT called in either panel component's server-side HTTP path
- `builder.Services.AddHttpClient("InternalPreview", ...)` registered in Program.cs with BaseAddress `http://localhost/`
- The relative URL (starting with `/api/artifacts/...`) is passed directly to `http.GetAsync()`
- `dotnet build` passes with 0 errors from `/home/fredw/projects/fip/fait`
