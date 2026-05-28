# CC Brief: ADO4559 R4 — SSRF Redirect Bypass Fix

## Context
You are fixing a security vulnerability in FortressAI.Web. The SSRF guard validates the initial URL but HttpClient auto-follows redirects without re-checking each redirect destination through IsBlockedHost(). This allows bypass via 302 → http://169.254.170.2/v2/credentials/.

Two files need changes:
1. `src/FortressAI.Web/Program.cs` — Disable AllowAutoRedirect on WebFetch handler
2. `src/FortressAI.Web/Services/WebFetchClient.cs` — Widen IP ranges + manual redirect loop

---

## Fix 1 — Program.cs: Disable AllowAutoRedirect on the WebFetch handler

File: `src/FortressAI.Web/Program.cs`

Find this block (around line 312-317):
```csharp
// Named HttpClient for WebFetch — enforces 3-redirect limit per spec
builder.Services.AddHttpClient("WebFetch")
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 3
    });
```

Replace it with:
```csharp
// Named HttpClient for WebFetch — redirects handled manually in WebFetchClient (SSRF re-validation)
builder.Services.AddHttpClient("WebFetch")
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        AllowAutoRedirect = false,       // redirects handled manually in WebFetchClient
        MaxAutomaticRedirections = 3     // no-op with AllowAutoRedirect=false, documents intent
    });
```

---

## Fix 2 — WebFetchClient.cs: Two changes

File: `src/FortressAI.Web/Services/WebFetchClient.cs`

### Part A: Replace IsBlockedHost() entirely

Find the entire `private static bool IsBlockedHost(string host)` method and replace it with:

```csharp
private static bool IsBlockedHost(string host)
{
    if (string.IsNullOrEmpty(host)) return true;
    var h = host.ToLowerInvariant().TrimEnd('.');

    // Full loopback range (not just 127.0.0.1)
    if (h == "localhost" || h.StartsWith("127.") || h == "::1") return true;

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

    // IPv6 link-local (fe80::/10)
    if (h.StartsWith("fe80:") || h.StartsWith("fe80::")) return true;

    // IPv6 ULA (fc00::/7 — covers fc00:: and fd00::)
    if (h.StartsWith("fc") || h.StartsWith("fd")) return true;

    // IPv4-mapped forms (::ffff:127.x, ::ffff:169.254.x)
    if (h.StartsWith("::ffff:127.") || h.StartsWith("::ffff:169.254.")) return true;
    if (h.StartsWith("0:0:0:0:0:ffff:127.") || h.StartsWith("0:0:0:0:0:ffff:169.254.")) return true;

    return false;
}
```

### Part B: Replace the HTTP call inside FetchAsync with a manual redirect loop

In `FetchAsync`, find this block (the portion that creates the request and gets the response — starts after `var client = _httpClientFactory.CreateClient("WebFetch");` and ends before the `if (!response.IsSuccessStatusCode)` check):

The current code:
```csharp
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            request.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            request.Headers.Add("Accept-Language", "en-US,en;q=0.9");

            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
```

Replace it with a manual redirect loop:
```csharp
            // Manual redirect loop — re-validates each redirect destination through SSRF guard
            const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
            var currentUrl = url;
            int redirectCount = 0;
            const int maxRedirects = 3;
            HttpResponseMessage? response = null;

            while (true)
            {
                var request = new HttpRequestMessage(HttpMethod.Get, currentUrl);
                request.Headers.Add("User-Agent", UserAgent);
                request.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
                request.Headers.Add("Accept-Language", "en-US,en;q=0.9");

                response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

                if ((int)response.StatusCode is >= 301 and <= 308)
                {
                    if (redirectCount >= maxRedirects)
                        return new WebFetchResult(false, null, null, "Too many redirects.", false);

                    var location = response.Headers.Location?.ToString();
                    if (string.IsNullOrEmpty(location))
                        return new WebFetchResult(false, null, null, "Redirect with no Location header.", false);

                    // Resolve relative redirect URLs against current URL
                    if (!Uri.TryCreate(location, UriKind.Absolute, out var absLocation))
                    {
                        if (!Uri.TryCreate(new Uri(currentUrl), location, out absLocation))
                            return new WebFetchResult(false, null, null, "Invalid redirect URL.", false);
                        location = absLocation.ToString();
                    }

                    // Re-validate redirect destination through SSRF guard
                    if (!Uri.TryCreate(location, UriKind.Absolute, out var redirectUri) ||
                        (redirectUri.Scheme != "http" && redirectUri.Scheme != "https") ||
                        IsBlockedHost(redirectUri.Host))
                    {
                        return new WebFetchResult(false, null, null,
                            "Redirect destination is not a permitted fetch target.", false);
                    }

                    currentUrl = location;
                    redirectCount++;
                    response.Dispose();
                    continue;
                }

                break; // Non-redirect response — proceed with this response
            }
```

The rest of FetchAsync after the response is obtained (status code check, body reading, HTML parsing, 2MB limit, JS heuristic, markdown conversion) is UNCHANGED. Only replace the single HttpRequestMessage creation + client.SendAsync call with the redirect loop above.

---

## Constraints
- Do NOT change anything else in these files
- Do NOT touch any other files
- Keep the existing timeout CancellationToken (cts) wiring intact — it threads through SendAsync as before
- Keep the existing using/disposal patterns

## Acceptance Criteria
1. Program.cs: `AllowAutoRedirect = false` on the WebFetch HttpClientHandler
2. WebFetchClient.cs: IsBlockedHost() blocks full 127.x loopback range, all IPv6 link-local (fe80:), ULA (fc/fd), IPv4-mapped forms
3. WebFetchClient.cs: FetchAsync uses manual redirect loop, calls IsBlockedHost() on each Location header before following
4. `dotnet build` in /home/fredw/projects/fip/fait produces 0 new errors

## Output
After making changes, run:
```
cd /home/fredw/projects/fip/fait && dotnet build --nologo 2>&1 | tail -20
```
Report the build result.
