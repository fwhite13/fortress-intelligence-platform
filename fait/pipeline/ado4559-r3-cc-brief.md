# CC Brief: ADO4559 R3 — SSRF Guard for WebFetchClient

## Task
Apply an SSRF security fix to `WebFetchClient.cs` in the FAIT project.

## File to Modify
`/home/fredw/projects/fip/fait/src/FortressAI.Web/Services/WebFetchClient.cs`

## What to Do

### Step 1: Add a private static `IsBlockedHost(string host)` method to the `WebFetchClient` class

Add this method inside the `WebFetchClient` class, after the constructor and before `FetchAsync`:

```csharp
private static bool IsBlockedHost(string host)
{
    if (string.IsNullOrEmpty(host)) return true;
    var h = host.ToLowerInvariant().TrimEnd('.');

    // Loopback
    if (h == "localhost" || h == "127.0.0.1" || h == "::1") return true;

    // ECS Fargate IMDS (highest priority — vends IAM credentials)
    if (h == "169.254.170.2") return true;

    // EC2 IMDS (defense-in-depth)
    if (h == "169.254.169.254") return true;

    // All link-local (169.254.x.x)
    if (h.StartsWith("169.254.")) return true;

    // RFC-1918 private ranges
    if (h.StartsWith("10.")) return true;
    if (h.StartsWith("192.168.")) return true;
    if (System.Text.RegularExpressions.Regex.IsMatch(h, @"^172\.(1[6-9]|2\d|3[01])\.")) return true;

    // Internal hostnames
    if (h.EndsWith(".internal") || h.EndsWith(".local") || h.EndsWith(".localhost")) return true;

    return false;
}
```

### Step 2: Add the SSRF guard at the very top of `FetchAsync`, before any HttpClient usage

The current `FetchAsync` signature is:
```csharp
public async Task<WebFetchResult> FetchAsync(string url, CancellationToken ct = default)
{
    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
```

Change it so the guard runs FIRST, before the `using var cts` line:

```csharp
public async Task<WebFetchResult> FetchAsync(string url, CancellationToken ct = default)
{
    // SSRF guard — validate URL before issuing request
    if (!Uri.TryCreate(url, UriKind.Absolute, out var parsedUri) ||
        (parsedUri.Scheme != "http" && parsedUri.Scheme != "https") ||
        IsBlockedHost(parsedUri.Host))
    {
        return new WebFetchResult(
            Success: false,
            Title: null,
            MarkdownContent: null,
            ErrorMessage: $"URL is not a permitted fetch target.",
            IsJsRendered: false);
    }

    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
```

## Constraints
- Do NOT modify any other files
- Do NOT change any other logic in WebFetchClient.cs
- Do NOT change method signatures, the interface, or the WebFetchResult record
- Only add IsBlockedHost() and the guard block at the top of FetchAsync
- Preserve all existing code exactly as-is below the guard insertion point

## Expected Result
After the change, `FetchAsync` will:
1. First check if the URL is valid, uses http/https, and targets a non-blocked host
2. If blocked → return WebFetchResult with Success=false and the error message
3. If allowed → proceed with the existing logic unchanged

The file compiles with 0 errors.
