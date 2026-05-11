# Review Report — ADO#3248

**Commit:** `ec8ed7ff`
**Date:** 2026-05-11
**Reviewer:** Hawkeye (Clint Barton)

### Verdict: ✅ PASS

---

### CC Review Summary

CC (Sonnet, targeted 2-question adversarial brief) read `fait-v2/agent-harness/harness-server.js` and verified the localhost routing change for the Brave web_search endpoint. Both questions resolved clean. No issues found.

---

### Spec Compliance Check

**What changed:**
- `/tools/web_search` now calls `http://localhost:${BLAZOR_INTERNAL_PORT||8080}/internal/mcp/brave` instead of `${FAIT_BASE_URL}/internal/mcp/brave`
- Bypasses Cloudflare for the Brave sidecar co-located in the same ECS task

**Files changed:**
- `fait-v2/agent-harness/harness-server.js` — ✅ confirmed as the only changed file

**Acceptance criteria:**
- [x] Brave MCP fetch uses `http://localhost:${port}/internal/mcp/brave` — ✅ lines 1197–1198
- [x] `BLAZOR_INTERNAL_PORT` env var respected with `8080` default — ✅
- [x] All fetch options (headers, body, method) unchanged — ✅ verified
- [x] `node --check` — syntax OK per build report

**Spec compliance verdict:** ✅ COMPLIANT

---

### Consistency Audit

**Cross-file check: harness URL → Blazor endpoint**

| Caller | Target | Status |
|--------|--------|--------|
| `harness-server.js` `POST http://localhost:8080/internal/mcp/brave` | `fait/src/FortressAI.Web/Program.cs` `app.MapPost("/internal/mcp/brave", ...)` | ✅ Endpoint exists (added in ADO#3240 cycle 3) |

**Port consistency:**
| Value | Source | Status |
|-------|--------|--------|
| `BLAZOR_INTERNAL_PORT \|\| '8080'` | harness-server.js line 1197 | ✅ Matches Blazor Dockerfile `ASPNETCORE_URLS=http://+:8080` |
| `FAIT_BASE_URL \|\| 'http://localhost:8080'` | harness-server.js line 57 | ✅ Both default to 8080 — consistent |

---

### Q7: BLAZOR_INTERNAL_PORT default — is 8080 correct?

`FAIT_BASE_URL` default is `http://localhost:8080` (line 57). The new `BLAZOR_INTERNAL_PORT` defaults to `'8080'` (line 1197). Both reference the same port.

FAIT Dockerfile: `ASPNETCORE_URLS=http://+:8080` and `EXPOSE 8080`. In ECS Fargate same-task deployment, the harness container and Blazor container share a network namespace — `localhost:8080` reaches the Blazor process directly, bypassing Cloudflare/ALB entirely.

The comment in the commit confirms this design: *"Use localhost to bypass Cloudflare (Brave endpoint is co-located in the Blazor container)"*.

**Verdict: ✅ Clean.** Port 8080 is correct. `BLAZOR_INTERNAL_PORT` is a proper override escape hatch.

---

### Q8: X-Internal-Token header presence

The `/tools/web_search` handler:
```js
const internalToken = INTERNAL_API_TOKEN;  // module-level const from process.env.INTERNAL_API_TOKEN || ''
// ...
headers: {
    'Content-Type': 'application/json',
    ...(internalToken ? { 'X-Internal-Token': internalToken } : {}),
}
```

- Header is conditionally sent: only if `INTERNAL_API_TOKEN` is non-empty
- If `INTERNAL_API_TOKEN` is unset → header omitted → Blazor returns 503/401 → harness throws → `/tools/web_search` returns 500 with error message to the model
- This is the correct failure mode — consistent with all other internal tool handlers

`internalToken` references the module-level `INTERNAL_API_TOKEN` const (not a fresh env read). Value is stable for the process lifetime.

**Verdict: ✅ Clean.** Header is sent when token is configured. Graceful degradation when not.

---

### Issues Found

None. No blockers, no important issues.

---

### Spec Fidelity

The change is surgical and correct. The Brave MCP endpoint at `localhost:8080/internal/mcp/brave` exists (added in ADO#3240 cycle 3), auth is validated with `X-Internal-Token`, and the response shape `{ content: [{ type: "text", text: ... }] }` is handled correctly by the harness consumer.

The Cloudflare bypass is the right architectural decision for internal container-to-container calls in the same ECS task.

---

_Hawkeye (Clint Barton) — PASS. Ready to ship._
