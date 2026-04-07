# FIP Architecture Overview — Discovery Agent Reference

## System Context
The Fortress Intelligence Platform (FIP) is a suite of AI-powered insurance tools. All modules share:
- Entra SSO auth (Microsoft Identity Web)
- Aurora MySQL databases
- AWS ECS Fargate deployment
- Bedrock AI integration (Claude Sonnet via cross-region inference)
- Azure Key Vault for secrets

## What "Novel" Means for FIP
When a spec submission proposes something novel for FIP, flag it for clarification:
- **External user access** — FIP is internal-only. External portal = novel, needs explicit confirmation.
- **Real-time data** — FIP tools are not real-time. Polling or async patterns are standard.
- **Non-AWS infrastructure** — all FIP infra is AWS. Azure/GCP integrations are novel.
- **Non-Entra auth** — Cognito, Auth0, social login = novel, needs explicit confirmation.
- **Mobile app** — FIP is web-only (Blazor Server). Native mobile = out of scope unless explicitly in spec.

## AI Integration Patterns
- Model: Claude Sonnet (us.anthropic.claude-3-5-sonnet-20241022-v2:0 or configured equivalent)
- All model IDs read from config — never hardcoded
- Bedrock Converse API for text generation
- Bedrock AgentRuntime for KB retrieval
- KB: FORGE-DevTeam-Shared (Bedrock Knowledge Base, backed by S3)

## Spec Quality Bar
A spec is complete when:
1. Auth model explicitly stated (Entra SSO confirmed, roles named)
2. Data model specified (tables, relationships, migration name)
3. All user flows have error/failure states
4. Acceptance criteria are testable (Given/When/Then format)
5. Out of scope explicitly listed (not just absent)
