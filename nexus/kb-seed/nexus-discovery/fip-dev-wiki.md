# FIP Development Wiki — Agent Reference

## Auth Patterns
**All FIP modules use Entra SSO (Microsoft.Identity.Web).** No Cognito. No exceptions.
- Auth is shared via cookie from `fip.fortressam.ai` portal
- No separate app registration per module — use the shared FIP Entra app
- Role enforcement via Entra claims, not local DB roles
- `NexusAdmin` claim for module-level admin functions

## Deployment Patterns
- All FIP modules deploy as ECS Fargate services on `fortress-tools-cluster`
- CodeBuild builds Docker images, pushes to ECR, ECS pulls new task defs
- Never build Docker images locally for FAIT/FIRM/FORMS/NEXUS/FAMOS — CodeBuild only
- Exception: `firm-vpbot` (Node/TypeScript) — local Docker build + ECR push

## Data Patterns
- All MySQL databases on Aurora (`fortress-ai-cluster`)
- Always include `GuidFormat=None` in connection strings (MySqlConnector requirement)
- Never use `HasColumnType("char(36)")` in EF fluent config — use `HasMaxLength(36)` for string properties
- All new tables use snake_case column names with explicit `HasColumnName()` mapping

## Module Inventory
| Module | Service | Purpose |
|--------|---------|---------|
| FAIT | fred-chat, fait-prod | AI chat, KB, meeting intelligence |
| FIRM | firm-web | Meeting recording + transcription |
| NEXUS | nexus-web | BA spec automation |
| FAMOS | famos-dev | Internal platform (FAM OS) |
| FORMS | forms-web | Form extraction (EAV) |
| vpbot | firm-vpbot | Meeting bot (Node/TypeScript) |

## What FIP Is NOT
- Not a member-facing portal — all tools are internal (Higginbotham/Fortress staff only)
- Not a public SaaS — deployed for a single enterprise customer (Refuge Group)
- Not a standalone product — all modules share auth, infrastructure, and deployment patterns
