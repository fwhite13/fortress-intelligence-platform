# Pipeline Completion: WI835

## Outcome: DEPLOYED ✅
**Date:** 2026-03-17
**Total pipeline time:** ~39 min (15:06 build → 15:45 confirm)

---

## What Shipped

FAIT Cowork Sprint 3 — FORGE injection, persistent instructions, task queue.

**CoworkAgent (cowork-agent:7 @ c4083da)**
- `services/taskQueue.ts` — Lua atomic `tryStartTask()` with `{userId}` hash tag for cluster mode; `onTaskFinished()` with floor-at-0 guard; cancel/queue-position helpers
- `routes/users.ts` — `GET/PUT /users/me/instructions` — persistent standing instructions per user
- `services/forgeClient.ts` — `buildSearchForgeMcpServer(userId, userEmail)` closure factory using `createSdkMcpServer` + Zod schema; FORGE cache key `cowork:forge-cache:${userId}:${hash}` (userId-scoped)
- `agent/runner.ts` — FORGE MCP server injected per task; persistent instructions prepended to system prompt (length-only audit log); cancellation check after each Agent SDK message
- `routes/tasks.ts` — queue integration in `POST /tasks` + `finally`; `DELETE /:id` cancel endpoint
- `services/taskStore.ts` — `_connectPromise ??=` guard for concurrent-safe ensureConnected
- `server.ts` — `/users` router registered

**CoworkWeb (cowork-web:7)**
- `Components/Pages/SettingsPage.razor` — standing instructions UI
- `Components/Shared/TaskQueue.razor` — running/queued badge, 10s poll timer, `IDisposable` Dispose()
- `Services/AgentApiClient.cs` — `GetInstructionsAsync`, `SaveInstructionsAsync`, `CancelTaskAsync`
- `Components/Layout/MainLayout.razor` — `<TaskQueue />` + Settings nav link

**CI Fix (c4083da):** `buildSearchForgeTool` → `buildSearchForgeMcpServer` using `createSdkMcpServer` (SDK requires `mcpServers` option, not `tools` for custom tools). Closure isolation preserved.

---

## Pipeline Summary

| Stage | Status | Notes |
|-------|--------|-------|
| PLAN | ✅ | Spec: COWORK-SPRINT3-SPEC.md |
| BUILD | ✅ | 1 cycle; commit 546e10a; 10 gate checks PASS |
| REVIEW | ✅ | Clint C1 PASS (13/13) + post-deploy diff CLEAR |
| SECURITY | ✅ | PASS — Lua atomic, userId isolation, instructions privacy |
| APPROVE | ✅ | Standing approval |
| DEPLOY | ✅ | CI fix c4083da (Agent SDK mcpServers API); cowork-web:7 + cowork-agent:7 |
| VERIFY | ✅ | Natasha PASS (8/8 infra; functional requires Fred auth) |
| CONFIRM | ✅ | WI#835 → Done |

---

## Follow-up Items (not blocking)
1. `double onTaskFinished()` on cancellation — concurrency can transiently drift +1; floor guard prevents negative; `_connectPromise` cache mitigates reconnect races (Clint advisory)
2. `topK` lower bound: `Math.max(1, topK)` guard missing (pre-existing, non-critical)
3. SSM plugin not installed on SteamServer — ECS private IP unreachable for Natasha's endpoint probes
4. Functional E2E (FORGE injection, task queue UI, settings page) requires Fred's authenticated session
5. Cowork `double onTaskFinished` — follow-up WI for proper idempotency guard

## MEMORY.md Update Needed
Agent SDK pattern: custom tools must use `createSdkMcpServer` + `mcpServers` option. `Options.tools` is `string[]` built-in tools only. Include in Tony's brief for Sprint 4.
