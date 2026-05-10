# DEPLOY-3217 — MCP Duplicate Registry Cleanup

**Date:** 2026-05-10  
**Agent:** War Machine (James Rhodes) — DevOps  
**Scope:** DB cleanup only — deleted 3 duplicate/dead MCP server entries from `fait_dev`

---

## Summary

Deleted 3 zero-tool duplicate entries from `mcp_servers` in `fait_dev`.  
No code changes, no Blazor build, no ECS deploy required.

---

## Rows Deleted

### `mcp_servers` — 3 rows deleted

| id | slug | name | Reason |
|----|------|------|--------|
| 3929f7ff-a6be-44e1-960d-a08cae1950c8 | ado | ado | Duplicate of `azdo` (Azure DevOps, 12 tools) |
| 8d1db093-1d1c-4225-bc1a-45c7e59c6b9d | ms365 | ms365 | Duplicate of `m365` (Microsoft 365, 5 tools) |
| 7edfb8da-1d3b-49d2-90ac-ad989ad6c554 | web_search | web-search | Duplicate of `brave` (Brave Web Search, 1 tool) |

### Child Table Cleanup

| Table | Rows Deleted | Notes |
|-------|-------------|-------|
| `conversation_mcp_servers` | 0 | No references to duplicate entries |
| `user_mcp_tokens` | 0 | No references to duplicate entries |
| `mcp_tool_call_log` | 0 | No references to duplicate entries |

> Note: `mcp_server_tools` table does not exist in `fait_dev` schema (tools stored inline on `mcp_servers`).  
> Child table FK column is `server_id` (not `mcp_server_id`).

---

## Final `mcp_servers` State

```
+----------+------------------+
| slug     | name             |
+----------+------------------+
| azdo     | Azure DevOps     |
| brave    | Brave Web Search |
| forge_kb | forge-kb         |
| m365     | Microsoft 365    |
+----------+------------------+
4 rows in set
```

✅ Exactly 4 rows — all working production entries. No duplicates remain.

---

## Verification

```sql
SELECT COUNT(*) FROM mcp_servers WHERE slug IN ('ado', 'ms365', 'web_search');
-- Result: 0 ✅
```

---

## Access Pattern Used

- SSH tunnel: `~/.ssh/fortress-bastion.pem` → `ec2-user@13.217.202.98`  
- Tunnel target: `fortress-ai-cluster.cluster-c89acukue4d5.us-east-1.rds.amazonaws.com:3306`  
- Local port: `3307`  
- DB user: `fortress_mysql`  
- Password: from `aws secretsmanager get-secret-value --secret-id fortress-tools/dev-db-password`
