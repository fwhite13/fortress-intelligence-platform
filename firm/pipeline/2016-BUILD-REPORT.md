# Build Report — ADO #2016
# FIRM: Service Connect callback URL + HF_TOKEN Secrets Manager

**Agent:** Tony Stark
**Cycle:** BUILD 1
**Date:** 2026-04-16
**Commit:** `332ead6`

---

## Fix 1 — Switch CallbackUrl to Service Connect DNS

### Changes

| File | Change |
|------|--------|
| `firm/src/FortressIntelligenceRM.Web/Services/BatchTranscriptionService.cs:46` | Fallback updated from `https://firm.dev.fortressam.ai/api/vp/callback` → `http://firm.fip.internal:8080/api/vp/callback` |
| `firm/src/FortressIntelligenceRM.Web/appsettings.json:30` | `CallbackUrl` updated from ALB DNS → `http://firm.fip.internal:8080/api/vp/callback` |

### Build Result

```
dotnet build firm/src/FortressIntelligenceRM.Web/
  18 Warning(s)
  0 Error(s)
Time Elapsed 00:00:04.66
```

**Status: SUCCEEDED**

### Commit & Push

```
332ead6 fix(ADO#2016): switch CallbackUrl to Service Connect DNS http://firm.fip.internal:8080
Pushed: origin/main
```

---

## Fix 2 — HF_TOKEN via Secrets Manager

### Status: BLOCKED — ARN Required

`fortress-tools-deployer` has no Secrets Manager permissions (AccessDeniedException). Tony cannot create or list secrets.

**Fred needs to either:**
1. Create secret `fortress-tools/hf-token` with the HF_TOKEN value (provided separately) in the AWS console and provide the ARN, **OR**
2. Confirm the existing ARN if the secret already exists.

Once ARN is provided, Tony will register a new Batch job definition revision with:
```json
"secrets": [
  {"name": "HF_TOKEN", "valueFrom": "arn:aws:secretsmanager:us-east-1:742932328420:secret:{SECRET_ARN}"}
]
```
...and remove `HF_TOKEN` from the static `environment` array.

**HF_TOKEN remains as a static env var in the current job def (:17) until ARN is supplied.**

---

## Acceptance Criteria

| # | Criteria | Status |
|---|----------|--------|
| 1 | `BatchTranscriptionService.cs` fallback = `http://firm.fip.internal:8080/api/vp/callback` | DONE |
| 2 | `appsettings.json` `Firm.CallbackUrl` = `http://firm.fip.internal:8080/api/vp/callback` | DONE |
| 3 | `dotnet build` → 0 errors | DONE |
| 4 | HF_TOKEN SM ARN blocker reported | DONE — awaiting Fred input |
