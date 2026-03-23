# WI#942 — Quote Scraper 401: Wrong auth header names in FAMOS

**Priority:** High — blocks quote scraping workflow
**Component:** FAMOS — Program.cs (FortressApi HttpClient config)
**Repo:** fip monorepo (`fip/famos/`)

## Root Cause (confirmed by live API test)
The Fortress API uses `apiKey` and `apiSecret` as header names (camelCase).  
FAMOS `Program.cs` registers the HttpClient with `X-Api-Key` and `X-Api-Secret` (wrong).

**Confirmed working:**
```bash
curl -X POST https://api.fortressam.ai/clients/internal/projects/internal_quote_scraper_cataloger/uploadLink \
  -H "apiKey: 246191f33f470f136ebb800516f8e10f" \
  -H "apiSecret: 77a883a60a2d941b0c1f038881150141dd3655f449c5dadf97e6ffb7066faf4d" \
  ...
# → HTTP 200, valid S3 upload URL returned
```

**Broken (current FAMOS code):**
```bash
curl ... -H "X-Api-Key: ..." -H "X-Api-Secret: ..." → HTTP 401
```

## Fix (Tony)
In `Program.cs`, lines ~134-139, change:
```csharp
c.DefaultRequestHeaders.Add("X-Api-Key",
    builder.Configuration["FortressApi:ApiKey"] ?? builder.Configuration["FortressApi:Key"] ?? "246191f33f470f136ebb800516f8e10f");
c.DefaultRequestHeaders.Add("X-Api-Secret",
    builder.Configuration["FortressApi:ApiSecret"] ?? builder.Configuration["FortressApi:Secret"] ?? "77a883a60a2d941b0c1f038881150141dd3655f449c5dadf97e6ffb7066faf4d");
```
To:
```csharp
c.DefaultRequestHeaders.Add("apiKey",
    builder.Configuration["FortressApi:ApiKey"] ?? builder.Configuration["FortressApi:Key"] ?? "246191f33f470f136ebb800516f8e10f");
c.DefaultRequestHeaders.Add("apiSecret",
    builder.Configuration["FortressApi:ApiSecret"] ?? builder.Configuration["FortressApi:Secret"] ?? "77a883a60a2d941b0c1f038881150141dd3655f449c5dadf97e6ffb7066faf4d");
```

## Natasha QA
1. Select carrier on FRANCIS TRANSPORTATION opportunity
2. Upload a real carrier quote PDF
3. Click "Upload & Scrape"
4. Verify no 401 — upload should progress (spinner, "Uploading..." state)
5. Poll for result (may take 30-60s)

## Acceptance Criteria
- No 401 on Upload & Scrape click
- PDF reaches Fortress API (S3 upload succeeds)
- Request submitted, polling begins
