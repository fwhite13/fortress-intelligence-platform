# WI#940 — Quote Scraper Upload returns 401 (Unauthorized)

**Priority:** High — blocks quote scraping workflow
**Component:** FAMOS — QuoteScraperService
**Repo:** fip monorepo (`fip/famos/`)

## What the User Sees
"Upload failed: Response status code does not indicate success: 401 (Unauthorized)" when clicking "Upload & Scrape" in the Quote PDF Scraper panel.

## Root Cause
Config key name mismatch between ECS task definition env vars and the code reading them.

**ECS task def sets:**
```
FortressApi__ApiKey    = 246191f33f470f136ebb800516f8e10f
FortressApi__ApiSecret = 77a883a60a2d941b0c1f038881150141dd3655f449c5dadf97e6ffb7066faf4d
FortressApi__Endpoint  = https://api.fortressam.ai
```

**Code reads (`Program.cs` lines 130-141):**
```csharp
builder.Configuration["FortressApi:Key"]    // ← looks for "Key" not "ApiKey"
builder.Configuration["FortressApi:Secret"] // ← looks for "Secret" not "ApiSecret"
builder.Configuration["FortressApi:BaseUrl"]// ← looks for "BaseUrl" not "Endpoint"
```

In .NET, `__` in env vars maps to `:` in config — so `FortressApi__ApiKey` becomes `FortressApi:ApiKey`, but the code reads `FortressApi:Key`. They don't match, so the fallback hardcoded values are used instead of the env-injected ones. Despite the fallback matching the current values, the BaseUrl also mismatches (`Endpoint` vs `BaseUrl`), which may cause the 401 if it's hitting the wrong endpoint.

## Fix (Tony)
**Option A (preferred) — fix the code to match existing env vars:**

In `Program.cs` lines 131-139, change:
```csharp
var fortressBase = builder.Configuration["FortressApi:BaseUrl"] ?? "https://api.fortressam.ai";
// ...
builder.Configuration["FortressApi:Key"] ?? "246191f33f470f136ebb800516f8e10f"
builder.Configuration["FortressApi:Secret"] ?? "77a883a60a2d941b0c1f038881150141dd3655f449c5dadf97e6ffb7066faf4d"
```
To:
```csharp
var fortressBase = builder.Configuration["FortressApi:Endpoint"] 
    ?? builder.Configuration["FortressApi:BaseUrl"] 
    ?? "https://api.fortressam.ai";
// ...
builder.Configuration["FortressApi:ApiKey"] 
    ?? builder.Configuration["FortressApi:Key"] 
    ?? "246191f33f470f136ebb800516f8e10f"
builder.Configuration["FortressApi:ApiSecret"] 
    ?? builder.Configuration["FortressApi:Secret"] 
    ?? "77a883a60a2d941b0c1f038881150141dd3655f449c5dadf97e6ffb7066faf4d"
```

This reads the actual env var names while keeping fallback compatibility.

## Acceptance Criteria
1. Selecting a carrier + PDF and clicking "Upload & Scrape" does not return 401
2. Upload progresses past the initial API call (even if scraper itself has downstream issues)
3. Fortress API key/secret confirmed being read from env vars (not hardcoded fallbacks)
