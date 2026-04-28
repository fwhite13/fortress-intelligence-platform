# RISE Deployment Spec — Refuge Notetaker

> **RISE** = Refuge Intelligence Suite for Enterprise
> **RN** = Refuge Notetaker (FIRM rebranded for Refuge)

## Overview

Deploy FIRM into the Refuge AWS environment as "Refuge Notetaker," fronted by a minimal RISE portal that owns Entra auth. Prod-only environment. Single codebase with FIRM as master — improvements push from Fortress → Refuge.

## Architecture

```
                     Fortress (existing)                    Refuge (new)
                     ──────────────────                    ────────────────
Portal URL:          fip.fortressam.ai                     portal.refugems.ai
Notetaker URL:       firm.fip.fortressam.ai                notetaker.refugems.ai
Entra Tenant:        7152ea12-c930-...                     d2bf3425-f8ab-...
Aurora Cluster:      fortress-ai-cluster                   refuge-ai-cluster (new)
AWS Account:         742932328420                           637131561301
Cookie Domain:       .fortressam.ai                        .refugems.ai
```

**Same codebase. Different env vars. Different branding.**

---

## Workstream 1: Branding Abstraction

FIRM currently has hardcoded "Fortress" references. These need to become config-driven.

### 1.1 FipShared Module Registry

`shared/FipShared/Models/FipModule.cs` currently hardcodes Fortress names and URLs:

```csharp
// Current:
FipModule.FIRM => "Fortress Intelligence & Risk Management"
FipModule.FIRM => "https://firm.fortressintelligence.com"
```

**Change:** Make `FullName()`, `ShortName()`, and `Url()` configurable via a `BrandingConfig` that modules read at startup. Each deployment provides its own branding JSON or env vars.

### 1.2 Branding Configuration

New config section in `appsettings.json` / env vars:

```json
{
  "Branding": {
    "SuiteName": "RISE",                           // "FIP" for Fortress
    "SuiteFullName": "Refuge Intelligence Suite",   // "Fortress Intelligence Platform"
    "ModuleName": "Refuge Notetaker",               // "FIRM" for Fortress
    "ModuleShortName": "RN",                        // "FIRM" for Fortress
    "OrgName": "Refuge",                            // "Fortress"
    "PortalUrl": "https://portal.refugems.ai",      // "https://fip.fortressam.ai"
    "ModuleUrl": "https://notetaker.refugems.ai",   // "https://firm.fip.fortressam.ai"
    "LogoPath": "/images/refuge-logo.svg",          // "/images/fortress-logo.svg"
    "AccentColor": "#1a2332"                        // keep or customize
  }
}
```

### 1.3 UI Touchpoints

Files with hardcoded branding references to update:

| File | Current | Configurable |
|------|---------|-------------|
| `MainLayout.razor` line 31 | `"Fortress"` | `@Branding.OrgName` |
| `MainLayout.razor` line 34 | `"FIRM"` | `@Branding.ModuleShortName` |
| `MainLayout.razor` line 41 | `"Fortress Intelligence Platform"` | `@Branding.SuiteFullName` |
| `OrgContext.razor` PageTitle | `"Settings — FIRM"` | `"Settings — @Branding.ModuleShortName"` |
| `FipNavBar` `ActiveModule` | `FipModule.FIRM` | dynamic from config |
| Health endpoint | `service = "firm"` | `service = Branding.ModuleShortName.ToLower()` |

### 1.4 Shared Theme

`FipTheme.cs` colors are fine as-is (dark navy + gold is neutral). If Refuge wants different colors, `Branding.AccentColor` / `Branding.PrimaryColor` can override the theme palette at startup. Not required for v1.

---

## Workstream 2: Refuge AWS Infrastructure

### 2.1 IAM Setup (Mirror Fortress Pattern)

Create in the Refuge AWS account:

| Resource | Name | Purpose |
|----------|------|---------|
| IAM User | `rise-deployer` | CI/CD builds + deploys (ECR push, ECS update, S3 access) |
| IAM User | `rise-bedrock` | Runtime Bedrock access (env vars injected into ECS tasks) |
| IAM Role | `ecsTaskExecutionRole-rise` | ECS task execution (pull ECR, write CloudWatch) |
| IAM Role | `ecsTaskRole-rise` | Runtime permissions (S3, Bedrock, Batch) |

**Permissions for `rise-deployer`:**
- `ecr:*` on RISE repos
- `ecs:UpdateService`, `ecs:DescribeServices`, `ecs:DescribeTaskDefinition`, `ecs:RegisterTaskDefinition`
- `s3:*` on RISE buckets
- `logs:*` on `/ecs/rise-*` log groups
- `batch:SubmitJob`, `batch:DescribeJobs` (for transcription pipeline)

**Permissions for `rise-bedrock`:**
- `bedrock:InvokeModel` on Claude models (cross-region inference profile)

### 2.2 Aurora MySQL

New Aurora MySQL Serverless v2 cluster in Refuge account:

| Resource | Value |
|----------|-------|
| Cluster | `refuge-ai-cluster` |
| Engine | Aurora MySQL 8.0 |
| Capacity | 0.5–4 ACU (serverless v2 — start small) |
| Schemas | `rise` (portal), `rn` (notetaker), `rn_fip` (shared token store) |
| Master user | `refuge_mysql` |
| Password | Secrets Manager `refuge-tools/db-password` |

FIRM's schema is created by `DatabaseInitializationService` (raw SQL, not EF migrations). Same code runs on first startup against the empty Refuge cluster.

### 2.3 ECS Cluster + Services

| Resource | Value |
|----------|-------|
| ECS Cluster | `rise-cluster` |
| ECR Repos | `rise-portal`, `rn-web` |
| Service: Portal | `rise-portal` → port 8080 |
| Service: Notetaker | `rn-web` → port 8080 |
| Task CPU/Memory | 512/1024 (same as Fortress FIRM) |

### 2.4 ALB + DNS

| Resource | Value |
|----------|-------|
| ALB | `rise-alb` |
| Certificate | ACM cert for `*.refugems.ai` |
| Rule 1 | `portal.refugems.ai` → `rise-portal` target group |
| Rule 2 | `notetaker.refugems.ai` → `rn-web` target group |

**DNS** (Route 53 in Refuge account — no CloudFlare):
- `portal.refugems.ai` → ALB CNAME
- `notetaker.refugems.ai` → ALB CNAME

### 2.5 S3 Buckets

| Bucket | Purpose |
|--------|---------|
| `refuge-notetaker-recordings` | Meeting audio files |
| `refuge-notetaker-transcripts` | Processed transcripts (if not using Aurora for storage) |

### 2.6 AWS Batch (Transcription Pipeline)

FIRM uses AWS Batch for transcription jobs (PyAnnote diarization + Bedrock summarization).

| Resource | Value |
|----------|-------|
| Job Queue | `rn-transcription-queue` |
| Job Definition | `rn-transcribe` |
| Compute Env | Fargate (same pattern as Fortress) |

The transcription container image is shared — push to Refuge ECR as `rn-transcribe`.

### 2.7 CloudWatch

| Log Group | Service |
|-----------|---------|
| `/ecs/rise-portal` | RISE portal |
| `/ecs/rn-web` | Refuge Notetaker |
| `/batch/rn-transcribe` | Transcription jobs |

---

## Workstream 3: Entra App Registration

### 3.1 Existing Refuge Entra App

An app registration already exists in the Refuge tenant:

| Property | Value |
|----------|-------|
| Client ID | `887206bc-fac1-436a-a8ed-2150418d76c0` |
| Tenant ID | `d2bf3425-f8ab-451c-83bd-1e0ebd9508fe` |
| Current Use | MS365 MCP server (Fred's Refuge email) |
| Current Redirect | `http://localhost:3333/callback` |

### 3.2 Rob/Patrick Ask — Additions Needed

**New redirect URIs (web platform):**
- `https://portal.refugems.ai/signin-oidc`
- `https://portal.refugems.ai/signout-callback-oidc`

**Additional API permissions (delegated):**
- `OnlineMeetings.Read` — Teams meeting detection, join URL extraction
- `User.ReadBasic.All` — participant name resolution in transcripts
- `Calendars.Read` — already granted ✅
- `Mail.Read` — already granted ✅ (not needed for Notetaker but exists)

**New client secret:**
- Generate a server-side secret for the ECS deployment (current secret is for local MCP delegated flow)
- Store in Secrets Manager: `refuge-tools/entra-client-secret`
- 12+ month expiry

**Optional (if not already configured):**
- Front-channel logout URL: `https://portal.refugems.ai/signout-callback-oidc`
- Token configuration: emit `oid` and `tid` claims in ID token

### 3.3 RISE Portal Auth Config

```json
{
  "AzureAd": {
    "TenantId": "d2bf3425-f8ab-451c-83bd-1e0ebd9508fe",
    "ClientId": "887206bc-fac1-436a-a8ed-2150418d76c0",
    "ClientSecret": "<from Secrets Manager>",
    "CallbackPath": "/signin-oidc",
    "SignedOutCallbackPath": "/signout-callback-oidc"
  },
  "Auth": {
    "CookieDomain": ".refugems.ai",
    "CookieName": ".RISE.Auth"
  }
}
```

RISE portal owns the OIDC flow and sets a domain-scoped cookie on `.refugems.ai`. Notetaker is a cookie consumer only (same pattern as Fortress: FIP portal → FIRM).

---

## Workstream 4: CI/CD — Build Once, Deploy Twice

### 4.1 Pipeline Design

```
git push (master) 
  → CodeBuild/ADO: Build Docker images
    → Push to Fortress ECR (existing)
    → Push to Refuge ECR (new — cross-account or separate push)
  → Deploy to Fortress ECS (existing pipeline)
  → Deploy to Refuge ECS (new deploy stage)
```

### 4.2 Cross-Account ECR Strategy

**Option A: Cross-account pull** (simpler)
- Build images in Fortress account
- Refuge ECS pulls from Fortress ECR via cross-account IAM policy
- Pro: single build, single image registry
- Con: runtime dependency on Fortress ECR availability

**Option B: Dual push** (more isolated)
- Build pushes to both Fortress and Refuge ECR
- Each environment is fully self-contained
- Pro: no cross-account runtime dependency
- Con: slightly more CI/CD config

**Recommendation:** Option B (dual push). Matches the "separate accounts" philosophy and avoids blast radius issues.

### 4.3 Environment-Specific Task Definitions

Each environment has its own ECS task definition with different env vars:

| Env Var | Fortress | Refuge |
|---------|----------|--------|
| `FORTRESS_DB_HOST` | `fortress-ai-cluster...` | `refuge-ai-cluster...` |
| `FIRM_DB_NAME` | `firm_dev` | `rn` |
| `FIP_DB_NAME` | `fip_dev` | `rn_fip` |
| `FIP__LoginUrl` | `https://fip.fortressam.ai` | `https://portal.refugems.ai` |
| `Auth__CookieDomain` | `.fortressam.ai` | `.refugems.ai` |
| `Branding__SuiteName` | `FIP` | `RISE` |
| `Branding__ModuleName` | `FIRM` | `Refuge Notetaker` |
| `Firm:S3Bucket` | `firm-recordings-dev` | `refuge-notetaker-recordings` |
| `Firm:ApiUrl` | `https://firm.fip.fortressam.ai` | `https://notetaker.refugems.ai` |
| `Firm:GraphTenantId` | `7152ea12-...` | `d2bf3425-...` |

---

## Workstream 5: RISE Portal (Minimal)

The RISE portal is a minimal ASP.NET Blazor app that:

1. Owns Entra OIDC login
2. Sets a domain-scoped auth cookie on `.refugems.ai`
3. Captures delegated Graph tokens and stores them in `rn_fip.user_microsoft_tokens`
4. Shows a single-tile app switcher (just "Notetaker" for now)
5. Redirects to `notetaker.refugems.ai` on tile click

**This is the FIP portal code deployed with Refuge config.** If FIP portal doesn't exist as a separate deployable yet (it doesn't — FAIT currently serves this role), we need to extract the auth-owner logic into a standalone portal app, or have FAIT serve this role for Refuge too.

**Practical approach:** Fork the minimal auth flow from FAIT into a lightweight `rise-portal` project in the FIP monorepo. It only needs:
- OIDC login/logout
- Token capture → DB
- DataProtection key ring (key owner for `.refugems.ai`)
- App switcher tile page
- Health endpoint

This is ~500 lines of code. FAIT's auth flow is the template.

---

## Workstream 6: Data Isolation Verification

Ensure FIRM code has no hardcoded references to Fortress-specific resources:

| Item | Status | Notes |
|------|--------|-------|
| DB connection | ✅ Config-driven | `FORTRESS_DB_HOST`, `FIRM_DB_NAME` env vars |
| S3 bucket | ✅ Config-driven | `Firm:S3Bucket` |
| Batch job queue/def | ⚠️ Check | May have hardcoded ARNs |
| Bedrock model IDs | ✅ Config-driven | `Bedrock:SummaryModelId` |
| FIP login URL | ✅ Config-driven | `FIP__LoginUrl` |
| Cookie domain | ✅ Config-driven | `Auth:CookieDomain` |
| Org wiki tenant ID | ✅ Config-driven | `Firm:GraphTenantId` |
| KB S3 prefix/IDs | ⚠️ Check | `Firm:PersonalKbId`, `Firm:TeamKbId` — these are Fortress Bedrock KB IDs |
| Teams Bot registration | ⚠️ Separate | Refuge needs its own Bot Framework registration |

### Bedrock Knowledge Bases

FIRM pushes transcripts to Bedrock Knowledge Bases (`PersonalKbId`, `TeamKbId`). These are Fortress-specific. Refuge needs:
- New Bedrock KBs in the Refuge account
- New S3 data sources
- Config: `Firm:PersonalKbId`, `Firm:TeamKbId` set to Refuge KB IDs

### Teams Bot

FIRM uses a Teams bot (Bot Framework) for meeting join. Refuge needs its own bot registration in the Refuge Entra tenant. This is a separate Rob/Patrick ask:
- New Bot Framework registration → Bot ID + secret
- Teams app manifest for Refuge org
- `Firm:BotCallbackSecret` env var

---

## Implementation Order

### Phase 1: Code Changes (no infra needed)
1. **Personal Wiki feature** (see `FIRM-PERSONAL-WIKI-SPEC.md`) — prereq
2. **Branding abstraction** — config-driven names/URLs throughout FIRM + FipShared
3. **RISE portal** — extract minimal auth-owner from FAIT into `rise-portal/`

### Phase 2: Refuge Infra (Rob/Patrick + Fred)
4. **Entra additions** — redirect URIs, permissions, new secret
5. **AWS infra** — Aurora, ECS, ALB, S3, Batch, IAM, DNS
6. **Bedrock KBs** — create in Refuge account

### Phase 3: Deploy
7. **CI/CD extension** — dual-push ECR, Refuge deploy stage
8. **First deploy** — RISE portal + Notetaker to Refuge ECS
9. **Smoke test** — login via `portal.refugems.ai`, record a meeting, verify transcription

### Phase 4: Teams Bot
10. **Bot registration** — Refuge Entra tenant
11. **Teams app manifest** — deploy to Refuge org
12. **Test end-to-end** — meeting join → record → transcribe → summarize

---

## Rob/Patrick Ask (Copy-Pasteable)

> **Subject: RISE Portal — Entra App Registration Updates**
>
> We're deploying a Refuge-branded version of our meeting assistant (FIRM → "Refuge Notetaker") behind a new portal at `portal.refugems.ai`.
>
> We have an existing Entra app registration in the Refuge tenant (Client ID: `887206bc-fac1-436a-a8ed-2150418d76c0`). We need the following additions:
>
> **1. Redirect URIs (Web platform):**
> - `https://portal.refugems.ai/signin-oidc`
> - `https://portal.refugems.ai/signout-callback-oidc`
>
> **2. Additional API Permissions (Delegated):**
> - `OnlineMeetings.Read`
> - `User.ReadBasic.All`
>
> **3. New Client Secret:**
> - For server-side use (ECS deployment)
> - 12+ month expiry preferred
>
> **4. Token Configuration:**
> - Ensure `oid` (object ID) and `tid` (tenant ID) claims are emitted in ID tokens
>
> **5. (Future) Bot Framework Registration:**
> - We'll need a Teams bot registration in the Refuge tenant for automated meeting join
> - Separate request when we're ready for that phase
>
> Please share the new client secret via secure channel (not email).

---

## Resolved

- **Refuge AWS account ID:** `637131561301`
- **Bedrock model access:** Same as Fortress — full `anthropic.claude-*` availability
- **VPC:** Single existing VPC `vpc-04e5e1d4df4e11806` — build inside it (ALB, ECS, Aurora subnets)
- **Environment:** Prod-only (no dev)

## Resolved (cont.)

- **Teams bot:** Not a factor for initial deployment. The Teams App manifest hasn't been deployed in Fortress either (Rob hasn't actioned it). The working bot is vpbot which doesn't require Entra. RN mirrors FIRM's current state; both get updated together when the Teams App integration is finalized.
- **Org wiki admin:** DB-based admin role. New `is_admin` column on `firm_users` table. No dependency on Entra role claims or group membership. Module admins are managed in each deployment's own DB. Part of the Personal Wiki prereq work (Task 0).
- **Subnets:** Will verify once Refuge IAM credentials are set up. Creating public + private subnets in `vpc-04e5e1d4df4e11806` if they don't exist.

## Implementation Prereqs (Ordered)

1. **FIRM Personal Wiki + DB Admin Role** — code changes, ships to Fortress first (see `FIRM-PERSONAL-WIKI-SPEC.md`)
2. **Refuge IAM Setup** — create `rise-deployer` + `rise-bedrock` users in account `637131561301`, configure credentials for CI/CD and runtime
3. Then proceed with Workstreams 1–6 above
