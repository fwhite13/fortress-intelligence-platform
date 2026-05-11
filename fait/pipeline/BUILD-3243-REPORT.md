# Build Report — ADO#3243

## What was built

**Verification pass** — ADO#3243 was filed to ensure `create_document` uses `FAIT_BASE_URL` (internal) instead of the public Cloudflare-fronted URL for the `generate-document` call. Upon investigation, the fix was already in place since ADO#3201 when the `create_document` handler was first introduced.

## Findings

### Code status: ✅ Already correct

The `create_document` handler at `/tools/create_document` (line 858 of `harness-server.js`) has used `FAIT_BASE_URL` since its initial implementation in commit `b167dc22` (ADO#3201). There was no `BLAZOR_BASE_URL` usage to migrate.

```js
// Line 76 — module-scoped constant
const FAIT_BASE_URL = process.env.FAIT_BASE_URL || 'http://localhost:8080';

// Line 874 — generate-document call
const genRes = await fetch(`${FAIT_BASE_URL}/api/workspace/generate-document`, {
    method: 'POST',
    headers,
    body: JSON.stringify({ type, title, sections })
});
```

### Full audit: All harness→Blazor API calls

| Line | Endpoint | Uses |
|------|----------|------|
| 94 | `/api/scheduled-tasks/approval/request` | `FAIT_BASE_URL` ✅ |
| 116 | `/api/intervention/request` | `FAIT_BASE_URL` ✅ |
| 774 | `/api/memory/search` | `FAIT_BASE_URL` ✅ |
| 804 | `/api/memory/read` | `FAIT_BASE_URL` ✅ |
| 838 | `/api/memory/write` | `FAIT_BASE_URL` ✅ |
| 874 | `/api/workspace/generate-document` | `FAIT_BASE_URL` ✅ |
| 1362 | `/api/memory/write` | `FAIT_BASE_URL` ✅ |
| 1431 | `/api/agents/:id/soul` | `FAIT_BASE_URL` ✅ |

**No hardcoded URLs, no `BLAZOR_BASE_URL` usage anywhere in `harness-server.js`.**

Self-calls to `http://localhost:${PORT}/tools/...` are internal harness routing (not Blazor) — correct.

### Root cause of the Cloudflare block

The Cloudflare block for `create_document` was caused by the deployed ECS task definition having `FAIT_BASE_URL=https://fait.fortressam.ai` (the public CDN URL) rather than the internal `https://fait.dev.fortressam.ai` endpoint. This was a deployment configuration issue, resolved as part of ADO#3223's harness task definition update.

## Files changed

None — code was already correct.

## `node --check` result

```
SYNTAX OK
```

## Parallelization used

N/A — verification pass only.

## CC sessions run

None — direct code audit via shell tools.

## Acceptance criteria verification

- [x] `generate-document` call uses `FAIT_BASE_URL` — **verified (line 874, was already correct since ADO#3201)**
- [x] No other harness→Blazor calls use hardcoded URLs or `BLAZOR_BASE_URL` — **verified (full audit above)**
- [x] `node --check` passes — **SYNTAX OK**

## Closing recommendation

ADO#3243 should be marked **Resolved** — the fix was already in place. No code change required. The Cloudflare issue in production was an env var configuration problem fixed by ADO#3223's harness task def update.

## Rhodey deployment note

No harness rebuild required for ADO#3243 — code is unchanged. Rhodey can close this alongside the #3242 deployment already queued.
