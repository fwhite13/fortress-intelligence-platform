# NEXUS P0 Deploy Report — fbc0b0d

**Date:** 2026-04-02 (UTC) / 2026-04-01 22:48 EDT  
**Operator:** War Machine (James Rhodes)  
**Outcome:** ❌ DEPLOY FAILED — Rollback to prior state  
**WIs:** #1515, #1516, #1517, #1521

---

## Pre-Deploy Snapshot (nexus-web:1 baseline)

```json
{
  "revision": 1,
  "image": "742932328420.dkr.ecr.us-east-1.amazonaws.com/nexus-web:latest",
  "env": [
    "AWS_REGION", "FORTRESS_DB_PORT", "ASPNETCORE_ENVIRONMENT", "FIP_DB_NAME",
    "Auth__CookieDomain", "FORTRESS_DB_USER", "Auth__CognitoAuthority",
    "UseStubAuth", "ASPNETCORE_URLS", "Auth__CognitoClientSecret",
    "FIP__LoginUrl", "FORTRESS_DB_HOST", "FRED_DB_NAME", "Auth__CognitoClientId"
  ]
}
```

**Note:** Pre-deploy, `nexus-web:3` was the actual running deployment (COMPLETED) — it was a prior deploy from 2026-03-31. `nexus-web:1` was the service's registered task definition.

---

## Build

| Field | Value |
|-------|-------|
| CodeBuild Project | `fip-nexus-build` |
| Source Version | `fbc0b0d` |
| Build ID | `fip-nexus-build:ab34fbe8-74ae-41eb-8380-f8aecb05f485` |
| Build Status | ✅ SUCCEEDED |
| Build Duration | ~2 minutes (QUEUED→BUILD→COMPLETED in ~123s) |
| Full Commit SHA | `fbc0b0d1c75a5da523054e06622936635ff05a43` |
| ECR Image | `742932328420.dkr.ecr.us-east-1.amazonaws.com/nexus-web:fbc0b0d1c75a5da523054e06622936635ff05a43` |

---

## Task Definition Changes (nexus-web:4)

**Registered:** `arn:aws:ecs:us-east-1:742932328420:task-definition/nexus-web:4`  
*(Note: Brief called this ":2" but AWS assigned :4 due to prior revisions :2 and :3)*

### Environment Variable Changes from nexus-web:1

**Removed (Cognito vars stripped):**
- `Auth__CognitoAuthority`
- `Auth__CognitoClientSecret`
- `Auth__CognitoClientId`

**Added:**
- `KeyVaultSettings__VaultUri=https://placeholder.vault.azure.net/`

**Retained env vars in nexus-web:4:**
```
ASPNETCORE_ENVIRONMENT=Development
ASPNETCORE_URLS=http://+:8080
AWS_REGION=us-east-1
Auth__CookieDomain=.dev.fortressam.ai
FIP_DB_NAME=nexus
FIP__LoginUrl=https://fip.dev.fortressam.ai
FORTRESS_DB_HOST=fortress-ai-cluster.cluster-c89acukue4d5.us-east-1.rds.amazonaws.com
FORTRESS_DB_PORT=3306
FORTRESS_DB_USER=fortress_mysql
FRED_DB_NAME=nexus
KeyVaultSettings__VaultUri=https://placeholder.vault.azure.net/
UseStubAuth=false
```

---

## ECS Deploy

| Field | Value |
|-------|-------|
| Cluster | `fortress-tools-cluster` |
| Service | `nexus-web` |
| Task Def | `nexus-web:4` |
| Deploy Start | ~22:52 EDT |
| Outcome | ❌ FAILED — 2 task failures (exit code 139 / segfault) |
| Stabilization Time | N/A — timed out at 600s |

### Failure Root Cause

**Exit code 139 (SIGSEGV)** — Container crashed on startup.

**CloudWatch logs reveal:**
```
Azure.Identity.DefaultAzureCredential.GetTokenFromSourcesAsync(...) 
→ Azure.Security.KeyVault.Secrets.SecretClient.GetPropertiesOfSecrets()
→ Azure.Extensions.AspNetCore.Configuration.Secrets.AzureKeyVaultConfigurationProvider.Load()
→ Program.<Main>$(String[] args) in /src/nexus/src/FortressNexus.Web/Program.cs:line 95
```

**The app attempts to connect to Azure Key Vault at startup** using `DefaultAzureCredential`. 
The `KeyVaultSettings__VaultUri=https://placeholder.vault.azure.net/` triggered a real connection attempt. 
`DefaultAzureCredential` found no valid Azure credentials in the ECS Fargate environment and the SDK 
crashed (exit 139) trying to exhaustively probe all credential sources including IMDS.

**This is a code issue, not an infra issue.** The Key Vault integration is hardwired to load at startup 
without a guard for missing/placeholder URIs.

---

## Rollback

| Field | Value |
|-------|-------|
| Rollback Target | `nexus-web:1` (per brief) |
| Rollback Command | `aws ecs update-service --cluster fortress-tools-cluster --service nexus-web --task-definition nexus-web:1 --force-new-deployment` |
| Rollback Triggered | Automatically on 600s timeout |
| Service Status | ✅ Healthy — `nexus-web:3` served traffic throughout, `nexus-web:1` rollback completing |

**Note:** `nexus-web:3` maintained `runningCount=1` throughout the entire failed deployment cycle — 
the service was never fully down. Health check at conclusion: **HTTP 200**.

---

## Health Check

```
curl -sk -o /dev/null -w "%{http_code}" https://nexus.fortressam.ai/health
→ 200 ✅
```

---

## Rollback Commands (if needed)

```bash
source /home/fredw/projects/ai/projects/fortress_tools/.env.deployer
export AWS_ACCESS_KEY_ID AWS_SECRET_ACCESS_KEY AWS_DEFAULT_REGION=us-east-1

# Roll back to :1
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service nexus-web \
  --task-definition nexus-web:1 \
  --force-new-deployment \
  --region us-east-1

# Roll back to :3 (was actually running before this deploy attempt)
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service nexus-web \
  --task-definition nexus-web:3 \
  --force-new-deployment \
  --region us-east-1
```

---

## Required Fix Before Re-Deploy

The application code at `Program.cs:95` must guard the Key Vault integration:

**Option A (Recommended):** Skip Key Vault if URI is not configured or is `placeholder`:
```csharp
var kvUri = builder.Configuration["KeyVaultSettings:VaultUri"];
if (!string.IsNullOrEmpty(kvUri) && !kvUri.Contains("placeholder"))
{
    builder.Configuration.AddAzureKeyVault(new Uri(kvUri), new DefaultAzureCredential());
}
```

**Option B:** Use a different env var to explicitly enable/disable Key Vault (`KeyVaultSettings__Enabled=false`).

**Option C:** Provide real Azure Key Vault URI + managed identity / workload identity for ECS.

Once the code fix is in place, a new build from the patched commit can be deployed.

---

## WI Status

| WI | Status | Action |
|----|--------|--------|
| #1515 | Open | Not resolved — deploy failed |
| #1516 | Open | Not resolved — deploy failed |
| #1517 | Open | Not resolved — deploy failed |
| #1521 | Open | Not resolved — deploy failed |

---

## Timeline

| Time (EDT) | Event |
|------------|-------|
| 22:48 | Deploy initiated |
| 22:49 | CodeBuild started (fip-nexus-build:ab34fbe8) |
| 22:51 | Build SUCCEEDED (~2 min) |
| 22:51 | ADO comment #1517 posted |
| 22:51 | Full SHA resolved: `fbc0b0d1c75a5da523054e06622936635ff05a43` |
| 22:52 | nexus-web:4 registered |
| 22:52 | ECS update-service nexus-web → nexus-web:4 |
| 22:52–23:02 | Multiple task launch attempts — all exit code 139 |
| 23:02 | 600s timeout — rollback triggered to nexus-web:1 |
| 23:03 | Health check: HTTP 200 ✅ (nexus-web:3 still serving) |
