# Build Report — ADO#2832

## What was built
Added `GET /mcp/health` as a public (no-auth) route to `services/fip-mcp/src/server.js`. This mirrors the existing `/health` route but is accessible at the path the ALB listener rule (priority 14) already bypasses Entra auth for.

## Files changed
- `services/fip-mcp/src/server.js` — Added 5 lines: new route block inserted after `/health`, before the auth-gated `POST /mcp` route

## Commit
`dccfb37` — `feat(ADO#2832): add GET /mcp/health route (public, no auth)`

## Parallelization used
No — single-file change, sequential.

## CC sessions run
1 (CC Sonnet) — minimal diff, straightforward insertion.

## Acceptance criteria verification
- [x] `GET /mcp/health` exists in the file
- [x] No `authMiddleware` on the route
- [x] Returns `{ status: 'ok', version: VERSION }`
- [x] Appears at line 183, before `POST /mcp` (line 188)

## Known edge cases / things Clint should scrutinize
- None — this is a 5-line insertion identical in structure to the existing `/health` route
- ALB rule at priority 14 must already be in place for this to be reachable without Entra auth (separate infra task ADO#2832)

## How to test locally
```bash
cd services/fip-mcp
node src/server.js &
curl http://localhost:3000/mcp/health
# Expected: {"status":"ok","version":"..."}
```
