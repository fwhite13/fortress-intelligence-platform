# Build Report: WI941 — FAM OS Quote Scraper 401 Fix

**Date:** 2026-03-20
**Agent:** Tony Stark (software-engineer)
**Risk Level:** Low (3-line config key fix)
**CC Invocation:** `cat brief | claude --model sonnet --dangerously-skip-permissions -p`

---

## Summary

Fixed FortressApi config key mismatch causing 401 errors in FAM OS Quote Scraper. ECS env vars use `FortressApi__ApiKey` / `FortressApi__ApiSecret` / `FortressApi__Endpoint` (double-underscore = colon in .NET config). Code was reading the old key names (`Key`, `Secret`, `BaseUrl`), missing the ECS values, falling back to hardcoded defaults, hitting wrong endpoint → 401.

## Files Modified

- `famos/src/FamOs.Web/Program.cs` — lines 131-143

## Changes Made (via Claude Code CLI)

| Line | Before | After |
|------|--------|-------|
| 131 | `builder.Configuration["FortressApi:BaseUrl"]` | `builder.Configuration["FortressApi:Endpoint"] ?? builder.Configuration["FortressApi:BaseUrl"]` |
| 138 | `builder.Configuration["FortressApi:Key"]` | `builder.Configuration["FortressApi:ApiKey"] ?? builder.Configuration["FortressApi:Key"]` |
| 142 | `builder.Configuration["FortressApi:Secret"]` | `builder.Configuration["FortressApi:ApiSecret"] ?? builder.Configuration["FortressApi:Secret"]` |

All three preserve fallback chains — existing deployments without the new key names are unaffected.

## Commit

`ca5fd07` — pushed to `origin/main`

## Verification

```
131: var fortressBase = builder.Configuration["FortressApi:Endpoint"]
132:         ?? builder.Configuration["FortressApi:BaseUrl"]
138:         builder.Configuration["FortressApi:ApiKey"]
139:             ?? builder.Configuration["FortressApi:Key"]
142:         builder.Configuration["FortressApi:ApiSecret"]
143:             ?? builder.Configuration["FortressApi:Secret"]
```

✅ Endpoint read first, ApiKey read first, ApiSecret read first — each with fallback.

## Self-Review Checklist

- [x] Only 3 config reads changed — no scope creep
- [x] Fallback chains preserved — backward compatible
- [x] CC used for all changes
- [x] Verified via grep post-change
- [x] Committed and pushed

## Status: READY FOR REVIEW
