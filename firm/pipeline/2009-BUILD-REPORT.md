# Build Report — ADO #2009 (Build 2: firm-web)

**Agent:** Tony Stark (BUILD)
**Date:** 2026-04-16
**WI:** ADO #2009
**Service:** firm-web (`FortressIntelligenceRM.Web`)

---

## Change

**File:** `firm/src/FortressIntelligenceRM.Web/appsettings.json` (line 30)

```diff
- "CallbackUrl": "https://firm.dev.fortressam.ai/api/vp/callback",
+ "CallbackUrl": "http://fortress-tools-alb-487057611.us-east-1.elb.amazonaws.com/api/vp/callback",
```

Fixes the fallback `CallbackUrl` used by `BatchTranscriptionService` when the `Firm__CallbackUrl` env var is absent. The ECS task def env var already has the correct value; this change aligns the appsettings.json default.

---

## Build Gate

```
dotnet build — 0 errors, 18 warnings
```

---

## Commit

`bb235ba` — `fix(ADO#2009): fix hardcoded CallbackUrl fallback to ALB direct HTTP`

Pushed to `origin/main`.

---

## No Review

One-line config change, no logic — Clint review not required per brief.
