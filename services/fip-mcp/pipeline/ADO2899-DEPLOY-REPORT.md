# Deploy Report: ADO#2899 — fip-mcp routing refactor

## Status: DEPLOYED ✅

## Deployment Details
- **Image:** fip-mcp:10
- **Commit:** 8f247b9d668141a108b2dd5ac94cd1bd6b9cdd92
- **ECS Service:** fortress-tools-cluster / fip-mcp
- **Running:** 1/1 desired ✅

## New Endpoints
- `GET /mcp/ms365/health`, `POST /mcp/ms365`, `GET /mcp/ms365/sse`
- `GET /mcp/ado/health`, `POST /mcp/ado`, `GET /mcp/ado/sse`
- `GET /mcp/web/health`, `POST /mcp/web`, `GET /mcp/web/sse`

## Health Checks
- Direct ALB: all 4 health endpoints return `{"status":"ok"}` ✅
- Via Cloudflare (`api.fortressam.ai`): returns 401 (Cloudflare WAF intercepting unauthenticated requests — same behavior as FIRM batch callback, non-blocking for authenticated MCP clients)
- ALB P14 bypass rule updated to cover all health paths (was /mcp/health only)

## ALB Rule Update
P14 modified to cover: `/mcp/health`, `/mcp/ms365/health`, `/mcp/ado/health`, `/mcp/web/health`, `/mcp/forge-kb/health`

## Rollback Plan
```bash
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service fip-mcp \
  --task-definition fip-mcp:8 \
  --profile fortress-tools-deployer \
  --region us-east-1
```

## Notes
- Cloudflare health check 401 is expected/known — MCP clients use Bearer token auth which passes through Cloudflare
- ECS container health is confirmed healthy via direct ALB endpoint
