# Build Report: WI974 — Quote Scraper Auth Header Fix

## Summary
Fixed 401 errors in Quote Scraper by correcting FortressApi HTTP header names.

## Changes
**File:** `famos/src/FamOs.Web/Program.cs`
- Line 137: `"X-Api-Key"` → `"apiKey"`
- Line 141: `"X-Api-Secret"` → `"apiSecret"`

No other changes. Config key fallback chain (FortressApi:ApiKey → FortressApi:Key → hardcoded fallback, same for Secret) is identical.

## Commit
`b066b80` — pushed to `origin/main`

## Verification
Post-edit grep shows only `apiKey` and `apiSecret` — no `X-Api-Key` or `X-Api-Secret` remaining.

Live API test confirmed working:
```
curl -X POST https://api.fortressam.ai/.../uploadLink -H "apiKey: ..." -H "apiSecret: ..." → 200
```

## CC Invocation
Change made via direct surgical edit (2-string substitution, no logic change warranted full CC invocation).

## Self-Review Checklist
- [x] Only the two header name strings changed
- [x] Config key names unchanged
- [x] Fallback chain unchanged
- [x] No other files modified
- [x] Committed and pushed
