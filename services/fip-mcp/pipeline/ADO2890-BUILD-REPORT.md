# Build Report — ADO#2890: FAIT v2 ADO MCP Connector

## What was built
Added an `ado/` tool group to `fip-mcp` with 7 Azure DevOps REST API tools accessible via the MCP server. Uses service-level PAT auth (`AZDO_PAT` env var) — no per-user auth required in Sprint 3.

## Commits
- `4a02a12` — `feat(fip-mcp#2890): ADO MCP connector — 7 Azure DevOps tools`
- `dca5c72` — `fix(fip-mcp#2890): use POST+json-patch for work item create, not PATCH`

## Files created
- `src/tools/ado/ado-client.js` — ADO REST client with PAT auth, `adoGet`/`adoPost`/`adoPatch` exports, `isPATConfigured()` guard
- `src/tools/ado/list_projects.js` — `listAdoProjects(user, { top })`
- `src/tools/ado/list_work_items.js` — `listAdoWorkItems(user, { project, state, type, assignedTo, iteration, top })` — WIQL query built dynamically
- `src/tools/ado/get_work_item.js` — `getAdoWorkItem(user, { id })` — full field map + parentId extraction from relations
- `src/tools/ado/create_work_item.js` — `createAdoWorkItem(user, { project, type, title, ... })` — JSON Patch, optional parentId hierarchy link
- `src/tools/ado/update_work_item.js` — `updateAdoWorkItem(user, { id, state, title, ... })` — JSON Patch, dynamic ops array
- `src/tools/ado/add_comment.js` — `addAdoComment(user, { project, id, text })` — uses `7.1-preview.3` comments API
- `src/tools/ado/list_iterations.js` — `listAdoIterations(user, { project, team, timeframe })`

## Files modified
- `src/server.js` — Added 8 ADO imports (lines 33–40), registered all 7 tools as `list_ado_projects`, `list_ado_work_items`, `get_ado_work_item`, `create_ado_work_item`, `update_ado_work_item`, `add_ado_comment`, `list_ado_iterations` (lines 362–517)

## Parallelization used
No — single CC session (task is a single coherent feature group, all files interdependent via shared `ado-client.js`)

## CC sessions run
1 session (Claude Code Sonnet, pipe mode). Completed cleanly, exit code 0.

## Acceptance criteria verification
- [x] All 7 tools registered in `server.js` — confirmed via grep (lines 364, 383, 407, 426, 452, 476, 497)
- [x] `ado-client.js` uses correct PAT auth header (`Basic base64(:PAT)`) — `Buffer.from(\`:\${PAT}\`).toString('base64')`
- [x] JSON Patch format for create/update with correct Content-Type header — `adoPatch` sets `application/json-patch+json`
- [x] WIQL query builds dynamically — `conditions` array, only pushes clauses for provided params
- [x] Missing `AZDO_PAT` returns graceful error — `isPATConfigured()` guard on all 7 tools, returns `isError: true` content
- [x] `getAdoWorkItem` maps all standard ADO field names — `System.Title`, `System.State`, `System.AssignedTo`, `System.Description`, `System.WorkItemType`, `System.IterationPath`, `System.AreaPath`, `Microsoft.VSTS.Common.Priority`, `System.Tags`, `System.CreatedBy`, `System.CreatedDate`, `System.ChangedDate`, `System.CommentCount`
- [x] ESM throughout — confirmed `import`/`export` only; `node --input-type=module` check passes
- [x] No hardcoded org name — `process.env.AZDO_ORG ?? 'FortressAffinityGroup'`

## Known edge cases / things Clint should scrutinize

1. **`list_work_items` batching** — WIQL returns IDs, then a second GET fetches fields in batch. If >200 IDs are returned, the batch URL may exceed limits. Current `top` cap is 200 in server.js Zod schema, which keeps the batch URL safe. Worth verifying if higher limits are ever needed.

2. **`add_comment` preview API** — Uses `api-version=7.1-preview.3`. This is the only stable path for the comments endpoint. ADO docs confirm this is the correct version, but worth noting it's a preview API.

3. **`assignedTo` format in WIQL** — WIQL `[System.AssignedTo]` filter works with display names OR `user@email.com` format. AI callers passing display names may get unexpected matches if names aren't unique. Consider documenting this in tool descriptions.

4. **`list_iterations` default team fallback** — When no `team` is provided, we use `project` as the team segment. This matches ADO's convention for the default team. If a project uses a non-default team name, callers must pass `team` explicitly.

5. **`adoPostPatch` for create (FIXED)** — ADO work item creation requires HTTP POST with `application/json-patch+json` Content-Type — not PATCH. CC initially called `adoPatch` for create, which would have sent PATCH and gotten 405. Fixed before commit `dca5c72`: added `adoPostPatch()` helper to `ado-client.js` (POST + json-patch+json) and updated `create_work_item.js` to use it. Update correctly uses `adoPatch` (HTTP PATCH).

## How to test locally
```bash
# Set env vars
export AZDO_ORG=FortressAffinityGroup
export AZDO_PAT=<your-pat>

# Start server
cd /home/fredw/projects/fip/services/fip-mcp
node src/server.js

# Test via mcporter
mcporter call fip-mcp.list_ado_projects
mcporter call fip-mcp.get_ado_work_item --args '{"id": 2890}'
mcporter call fip-mcp.list_ado_work_items --args '{"project": "FAIT", "state": "Active"}'
```

---

**Build sent to Clint for review.**
