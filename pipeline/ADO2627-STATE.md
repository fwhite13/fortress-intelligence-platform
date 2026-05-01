# Pipeline State: ADO#2627

## Current Stage: BUILDING
## Risk Level: high (new service, new ECR repo, new ECS service, new ALB rule)
## Pipeline Path: full
## Review Cycles: 0

### WI
- **Title:** Build and deploy fip-mcp: FORGE KB MCP Server (Phase 0)
- **ADO ID:** 2627
- **Spec:** /home/fredw/.openclaw/workspace/memory/projects/forge-kb-mcp-server-spec-2026-04-27.md
- **Repo:** /home/fredw/projects/fip/services/fip-mcp/ (new service)

### Pre-build findings
- ECR repo fip-mcp: does NOT exist — Tony creates
- ECS service fip-mcp: does NOT exist — Rhodey creates (new service, not update)
- IAM policy FipMcpBedrockAccess: exists per WI, deployer can't iam:GetPolicy but WI guarantees it
- Entra values: from nexus-web task def (TenantId + ClientId) — inject as plain env vars same pattern
- Phase 0: fallback static entitlements, no FAIT v2 DB dependency

### Stage History
| Stage | Status | Agent | Started | Completed | Notes |
|-------|--------|-------|---------|-----------|-------|
| PLAN  | ✅ DONE | Maria | 10:57 | 11:00 | Spec read, pre-build checks done |
| BUILD | 🔄 ACTIVE | Tony | 11:00 | — | New Node.js MCP server |
