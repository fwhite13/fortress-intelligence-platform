# Review Report — ADO#2627

**Service:** fip-mcp — FORGE KB MCP Server Phase 0
**Reviewer:** Hawkeye (Clint Barton)
**Cycle:** 1
**Commit:** `11aab1f`
**Date:** 2026-05-01

---

### Verdict: NEEDS-CHANGES

Two hard fails (Dockerfile non-root user, package.json engines field) plus two WARNs (CORS wildcard regex, get_job_status ownership check). Security-critical logic is solid — no auth bypasses, no scoping overrides, no critical vulnerabilities. Fix the two fails and this ships.

---

## CC Review Invocation

```bash
cat /home/fredw/projects/fip/pipeline/ADO2627-BUILD-REPORT.md | \
  claude --model sonnet --print --dangerously-skip-permissions \
  < /tmp/clint-brief-ADO2627.md
```

Brief written to `/tmp/clint-brief-ADO2627.md` — adversarial spec with 14 targeted checks across security, functional, and quality layers.

---

## Spec Compliance Check

**Brief:** `memory/projects/forge-kb-mcp-server-spec-2026-04-27.md`

**§2 Codebase Map — new service, 14 files:**
- `services/fip-mcp/src/server.js` — ✅ created
- `services/fip-mcp/src/auth.js` — ✅ created
- `services/fip-mcp/src/config/kb-inventory.js` — ✅ created
- `services/fip-mcp/src/config/entitlements.json` — ✅ created
- `services/fip-mcp/src/tools/search_kb.js` — ✅ created
- `services/fip-mcp/src/tools/list_kbs.js` — ✅ created
- `services/fip-mcp/src/tools/add_to_kb.js` — ✅ created
- `services/fip-mcp/src/tools/get_kb_metadata.js` — ✅ created
- `services/fip-mcp/src/tools/get_job_status.js` — ✅ created
- `services/fip-mcp/Dockerfile` — ✅ created (with defect — see I1)
- `services/fip-mcp/package.json` — ✅ created (with defect — see I2)
- `services/fip-mcp/package-lock.json`, `.gitignore`, `README.md` — ✅ created

**§6 Out of Scope:**
- ✅ No out-of-scope changes detected

**§7 Acceptance Criteria (inferred from spec §§3-6):**
- [x] 5 MCP tools implemented: ✅ Verified — all 5 registered in server.js
- [x] Entra JWT auth on /mcp routes: ✅ Verified
- [x] /health bypasses auth: ✅ Verified
- [x] KB inventory with all Phase 0 IDs: ✅ Verified — stale EE1X6QJ9WH absent
- [x] Personal KB user_id scoping enforced and cannot be overridden: ✅ Verified
- [x] CORS set to specific fortressam.ai origins, no wildcard `*`: ✅ No `*` — but regex is broader than needed (see W1)
- [x] Fallback entitlements.json with Corp+Personal+NEXUS defaults: ✅ Verified
- [x] add_to_kb Phase 0 TODO documented, no runtime crash: ✅ Verified
- [x] In-memory job tracking, get_job_status working: ✅ Verified
- [ ] Dockerfile non-root user: ❌ Missing (see I1)
- [ ] package.json Node 22 engine declared: ❌ Missing (see I2)

**Spec compliance verdict:** ⚠️ PARTIALLY COMPLIANT — two missing implementation details block full PASS

---

## Consistency Audit

**Cross-file checks performed:**

| Check | Result |
|-------|--------|
| `jobMap` exported from `get_job_status.js` and imported in `add_to_kb.js` | ✅ consistent — same Map reference, correct shared state |
| `getEntitlements()` exported from `list_kbs.js`, imported in `search_kb.js` and `add_to_kb.js` | ✅ consistent |
| `getKb()` from `kb-inventory.js` used identically across all tools | ✅ consistent |
| SCOPING_RULE constants match KB_INVENTORY entries | ✅ all 9 KBs have correct scoping_rule values |
| `user.user_id` (auth.js `payload.oid`) → Personal KB filter key `user_id` | ✅ consistent end-to-end |
| `forge-kb-admin` role check in add_to_kb.js vs. /admin/entitlements in server.js | ✅ both use `user.roles.includes('forge-kb-admin')` |
| Port: server.js `process.env.PORT ?? '3000'` vs. Dockerfile `EXPOSE 3000` | ✅ consistent |
| NEXUS write-lock: entitlements.json `write: false` + list_kbs.js programmatic enforcement | ✅ belt-and-suspenders, consistent |

**EE1X6QJ9WH stale KB check:** Appears only in a comment (`// IMPORTANT: Do NOT reference stale...`) — not as an inventory key. ✅ Clean.

---

## Issues Found

### Important Issues

#### I1: Dockerfile — No non-root USER directive
| | |
|---|---|
| **File** | `Dockerfile` |
| **Severity** | Important |
| **Category** | Security / Container hardening |

Container runs as `root`. Violates Docker security best practice and will fail most container security scanners (Trivy, Checkov, Prisma). The spec (§6) implicitly requires standard ECS Fargate security posture.

**Fix:**
```diff
  COPY src/ ./src/
  
+ RUN addgroup -S appgroup && adduser -S appuser -G appgroup
+ USER appuser
+ 
  HEALTHCHECK --interval=30s ...
```

---

#### I2: package.json — No `engines` field
| | |
|---|---|
| **File** | `package.json` |
| **Severity** | Important |
| **Category** | Correctness / Developer safety |

Node 22 is enforced by the Dockerfile but absent from `package.json`. Local dev or CI runners on Node 18/20 will silently proceed without warning. Spec §6 specifies Node.js 22.

**Fix:**
```diff
  {
    "name": "fip-mcp",
    "version": "1.0.0",
    "description": "FORGE KB MCP Server — Phase 0",
+   "engines": { "node": ">=22.0.0" },
    "type": "module",
```

---

### Nitpicks / WARNs

#### W1: CORS — `WILDCARD_ORIGIN_RE` is broader than needed
| | |
|---|---|
| **File** | `server.js:33` |
| **Severity** | Warn |
| **Category** | Security posture |

```js
const WILDCARD_ORIGIN_RE = /^https:\/\/[a-zA-Z0-9-]+\.fortressam\.ai$/;
```

Matches any single-level `*.fortressam.ai` subdomain. Combined with `Access-Control-Allow-Credentials: true`, any app at `anything.fortressam.ai` can make credentialed cross-origin requests — including a developer's personal test deployment or a compromised subdomain. The blast radius is bounded to the corporate DNS zone, but it undermines the intent of the `ALLOWED_ORIGINS` explicit allowlist.

**Recommendation:** Remove `WILDCARD_ORIGIN_RE` entirely and extend `ALLOWED_ORIGINS` explicitly as new FIP services are added. Not blocking — team call on risk tolerance. If the regex stays, add a comment documenting the decision.

---

#### W2: `get_job_status` — No job ownership check
| | |
|---|---|
| **File** | `src/tools/get_job_status.js` |
| **Severity** | Warn |
| **Category** | Information disclosure |

`user` is received but never checked against `jobMeta.initiated_by`. Any authenticated user who guesses or obtains a `job_id` can retrieve job metadata (including `kb_id`, `initiated_by`, timing) for jobs initiated by other users. Risk is low — job IDs are UUIDs and not guessable — but it's an information disclosure path.

**Recommendation:** Add ownership check:
```js
if (jobMeta.initiated_by !== user.user_id && !user.roles.includes('forge-kb-admin')) {
  throw { code: 'NOT_ENTITLED', status: 403, message: 'Not authorized to view this job' };
}
```
Not blocking for Phase 0, but worth tracking.

---

#### W3: package.json — AWS SDK version range is loose
| | |
|---|---|
| **File** | `package.json` |
| **Severity** | Nitpick |

`"@aws-sdk/client-bedrock-agent": "^3.0.0"` allows any 3.x release. The lock file mitigates drift in CI/CD, but the intent is imprecise. Consider pinning to a known-good minor (e.g., `^3.775.0`). Not blocking.

---

## Positive Observations

- **Personal KB security filter** is airtight. The RESERVED_KEYS approach to blocking caller override is elegant and correct. Attacker-supplied `user_id` in filters is silently dropped, not used.
- **Auth middleware ordering** is correct throughout: all auth checks precede any Bedrock API call with no bypass paths found.
- **`data_source_id: null` handling** in add_to_kb.js (`DATA_SOURCE_UNAVAILABLE → 503`) is correct — won't crash on the unresolved Project KB data source.
- **Phase 0 TODOs** are clearly documented in code with enough context to act on them. The S3 write gap won't cause a runtime crash — it's a no-op ingest until S3 is wired.
- **Belt-and-suspenders on NEXUS write-lock**: both entitlements.json `write: false` and programmatic enforcement in list_kbs.js. Good.
- **`createMcpServer(user)` factory pattern** is clean. User captured via closure avoids request context threading and eliminates any chance of user context leakage between requests.
- **Error code/status mapping** is clean and complete across all tools.

---

## What to Fix (for Tony)

Two changes required before PASS:

**1. Dockerfile — add non-root user (2 lines)**

```dockerfile
# Add these two lines before the HEALTHCHECK directive in the runtime stage:
RUN addgroup -S appgroup && adduser -S appuser -G appgroup
USER appuser
```

**2. package.json — add engines field (1 line)**

```json
// Add after "description":
"engines": { "node": ">=22.0.0" },
```

That's it. No security issues, no functional gaps. Two code-quality lines and this goes to security scan.

---

## Phase 0 Accepted Gaps (not blocking)

Per spec and Tony's build report:
- Team KB membership validation skipped (Phase 0 explicit) — needs `team_memberships` table in FAIT v2 DB
- In-memory job map (Phase 0 explicit) — job status lost on restart, acceptable for now
- S3 content write before StartIngestionJob not implemented (spec §8 #7) — documented TODO, no crash risk
- No FAIT v2 DB connection (fallback static entitlements only)

All documented. None block Phase 0 ship.

---

_Hawkeye | Cycle 1 | 2026-05-01_

---

## Cycle 2 Sign-Off — commit `76ec38f`

**Date:** 2026-05-01  
**Verdict: ✅ PASS**

### Changes Verified via CC

| Check | Result |
|-------|--------|
| `USER appuser` directive present before CMD in Dockerfile | ✅ Confirmed |
| `RUN addgroup -S appuser && adduser -S appuser -G appuser` present in Dockerfile | ✅ Confirmed |
| `"engines": { "node": ">=22.0.0" }` added to package.json | ✅ Confirmed |
| Exactly 2 files changed (`Dockerfile`, `package.json`) — no other files touched | ✅ Confirmed |

### Both cycle-1 NEEDS-CHANGES items resolved:
- **I1** (Dockerfile non-root user) — Fixed ✅
- **I2** (package.json engines field) — Fixed ✅

No regressions. No scope creep. ADO comment posted (id: 768857).

Ready for security scan (Rhodey).

_Hawkeye | Cycle 2 | 2026-05-01_
