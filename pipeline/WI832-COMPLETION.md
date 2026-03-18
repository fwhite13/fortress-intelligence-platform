# Pipeline Completion: WI832

## Outcome: DEPLOYED ✅
**Date:** 2026-03-17
**Total pipeline time:** ~3h12m (09:05 build → 12:17 confirm) — includes session restart + 5 CI fix iterations

---

## What Shipped

FAIT Cowork Sprint 1 — two new ECS services on `fortress-tools-cluster`.

**CoworkWeb (.NET 9 Blazor Server)**
- FIP cookie auth consumer (same `.FortressAI.Session` cookie as FAIT/FIRM/FORMS)
- DataProtection: `SetApplicationName("FortressAI")` + `DisableAutomaticKeyGeneration()` — shared key ring
- Task creation UI (`NewTask.razor`), SSE stream consumer (`TaskPage.razor`)
- iframe sandbox `allow-scripts` only for HTML output rendering
- Internal JWT signing (`InternalTokenService`) for Blazor→Node auth
- `AgentApiClient` — HTTP proxy to CoworkAgent with per-request JWT injection

**CoworkAgent (Node.js TypeScript Express)**
- Agent SDK (`@anthropic-ai/claude-agent-sdk 0.2.77`) — Bedrock/Claude execution
- `POST /tasks` — starts Agent SDK task, returns taskId
- `GET /tasks/:id/stream` — SSE stream with close handler (cancelled flag prevents runaway on disconnect)
- JWT validation middleware (`COWORK_INTERNAL_SECRET` from env var, throws at module load)
- CloudWatch audit logging to `/cowork/tasks`
- FORGE kb-search with `x-user-id` header

**FipShared**
- `FipModule.Cowork` enum value + 3 switch cases — waffle menu will include Cowork after FAIT rebuild

**Infrastructure**
- ECR repos: `cowork-web` + `cowork-agent`
- CloudWatch log group: `/cowork/tasks` (90-day retention)
- ECS task defs: `cowork-web:4` + `cowork-agent:3`
- ECS services: both running 1/1 on `fortress-tools-cluster`
- fip commit: `9804313`

---

## Pipeline Summary

| Stage | Status | Notes |
|-------|--------|-------|
| PLAN | ✅ | Spec: COWORK-SPRINT1-SPEC.md |
| BUILD | ✅ | 2 review cycles; 5 CI fixes needed (missing Blazor scaffolding, TS SDK API mismatch) |
| REVIEW | ✅ | C2 PASS + post-deploy diff CLEAR; all security checks intact |
| SECURITY | ✅ | PASS — JWT no-fallback, iframe sandbox, bash per Fred approval |
| APPROVE | ✅ | Standing approval |
| DEPLOY | ✅ | Infra created; redeploy at 9804313 for .NET 9 fix |
| VERIFY | ✅ | Natasha — PASS (infra-only; no public URL in Sprint 1) |
| CONFIRM | ✅ | WI#832 → Done |

**Review cycles:** 2 | **Deploy iterations:** 2 | **Security findings:** None blocking

---

## Sprint 2 Prerequisites (flagged during deploy)
1. **IAM policy** — deployer account needs SecretsManager/SSM write perms to move `COWORK_INTERNAL_SECRET` out of plaintext ECS env var
2. **DataProtection key ring** — `ConnectionStrings__KeyRingDb` password has unescaped special chars; cookies won't persist across container restarts until fixed
3. **ALB + DNS** — `cowork.dev.fortressam.ai` / `cowork.fortressam.ai` (Sprint 2 scope)
4. **FAIT rebuild** — to pick up FipShared `FipModule.Cowork` change (Cowork in waffle menu)
5. **multer upgrade** — `multer@1.4.5-lts.2` deprecated; CVEs patched in 2.x

## Known: No public URL in Sprint 1
Cowork is infrastructure-only. Sprint 2 adds ALB, DNS, Redis, and approval gates.
