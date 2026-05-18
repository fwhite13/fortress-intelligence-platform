# QA Report: Epic 7 — fred-dev

**Date:** 2026-05-17  
**QA:** Natasha (Black Widow)  
**Duration:** ~35 minutes

---

## Verdict: ⚠️ PARTIAL PASS — HARNESS NOT DEPLOYED

---

## Environment

| Field | Value |
|-------|-------|
| URL | https://fait.dev.fortressam.ai |
| Blazor task def | fred-dev:217 |
| Blazor image | fred-chat:67bd5405 ✅ |
| Harness task def (referenced) | fait-v2-agent-harness:37 |
| Harness image (deployed) | fait-v2-agent-harness:3ee48cee ❌ |
| Harness task def (has Epic 7) | fait-v2-agent-harness:38 |
| Harness image (with Epic 7) | fait-v2-agent-harness:62f6a82c |
| ECS status | fred-dev: 1/1 RUNNING, HEALTHY |
| Deployment time | 2026-05-17 17:01 EDT |
| Browser access | Blocked by Cloudflare WAF (pre-existing, ongoing) |

---

## Critical Issue: Harness Not Updated

The `fred-dev:217` task definition references `fait-v2-agent-harness:37` (image `3ee48cee`, pushed **May 16 11:47**). All Epic 7 harness features were committed and built **May 17**, and exist only in images from commit `62f6a82c` onward (registered as `fait-v2-agent-harness:38`).

The harness task def `fait-v2-agent-harness:38` (image `62f6a82c`) **exists in ECR and is registered as a task definition**, but `fred-dev:217` was never updated to reference it.

### Features That Cannot Work (harness-dependent)

| Feature | Commit | Status |
|---------|--------|--------|
| 7.6 Per-turn model selection (harness side) | `aab17788` | ❌ NOT DEPLOYED |
| 7.4 write_file tool | `70241fda` + `0f6f70aa` + `62823204` | ❌ NOT DEPLOYED |
| 7.7 Tool manifest / structured system prompt | `6f8238bb` | ❌ NOT DEPLOYED |
| 7.3 pgvector memory system | `451a68f0` + `67bd5405` | ❌ NOT DEPLOYED |
| 7.2 KB retrieval for CC spawn path | `e9b5132f` + `9fca311b` | ❌ NOT DEPLOYED |

---

## Feature Results

| Feature | AC | Result | Notes |
|---------|----|---------|----|
| 7.6 Model Selection (UI) | Selector visible in chat | ✅ Code PASS | `ModelSelector.razor` present, wired in ChatView line 298; `HandleModelChanged` saves to DB |
| 7.6 Model Selection (harness) | Switch sends with new model | ❌ FAIL | Harness `3ee48cee` uses hardcoded `MODEL_ID` env var; ignores `Model` field from Blazor |
| 7.1 KB Toggle Flags | Scheduled tasks respect KB flags from project settings | ✅ Code PASS | `ScheduledTaskBackgroundService.cs` builds `KbFlags` from `proj.EnableFortressKb`/`EnablePersonalKb` (ADO#3394) |
| 7.4 Harness write_file | Write file to workspace | ❌ FAIL | Tool not present in deployed harness `3ee48cee` |
| 7.5 File Explorer UI | Explorer renders, context menu, version history, drag-drop | ✅ Code PASS | `WorkspaceFiles.razor` has full implementation: right-click menu (rename, delete, version history, move), inline rename, breadcrumb nav; browser blocked by CF |
| 7.7 Behavior Quality | Structured prompt, tool manifest | ❌ FAIL | `buildToolManifestSection()` not in deployed harness `3ee48cee` |
| 7.3 pgvector smoke | No crash on connect failure | ❌ NOT DEPLOYED | pgvector feature (3414-line harness) not in deployed `3ee48cee` (2713 lines) |
| 7.2 KB CC path | CC-mode sessions inject KB context | ❌ FAIL | CC KB retrieval (`ADO#3392`) not in deployed harness `3ee48cee` |

---

## ECS / Infrastructure Verification

| Check | Result |
|-------|--------|
| fred-dev:217 ACTIVE, 1/1 RUNNING | ✅ PASS |
| Image fred-chat:67bd5405 confirmed | ✅ PASS |
| ECS deploy completed at 17:01 EDT | ✅ PASS |
| Migrations completed (CloudWatch) | ✅ PASS (16:59:34) |
| ScheduledTaskBackgroundService started | ✅ PASS (16:59:34) |
| Zero fatal startup errors | ✅ PASS |
| Application health check responding | ✅ PASS (HEALTHY per ECS) |
| Cloudflare WAF blocking external access | ⚠️ ONGOING (pre-existing) |

---

## Code Verification (Blazor — fred-chat:67bd5405)

All Blazor/backend changes verified in source at HEAD (`67bd5405`):

| Change | Verified |
|--------|---------|
| `ModelSelector.razor` — model dropdown component | ✅ |
| `ChatView.razor` — ModelSelector wired at line 298, `HandleModelChanged` persists to DB | ✅ |
| `ChatView.razor` — `currentModel` loaded from conversation (line 548), persists in session | ✅ |
| `KbFlags` sent always (never null) per ADO#3316-fix | ✅ |
| `ScheduledTaskBackgroundService.cs` — builds explicit `KbFlags` from project settings (ADO#3394) | ✅ |
| `WorkspaceFiles.razor` — file explorer with right-click context menu (rename, delete, version history, move) | ✅ |
| `WorkspaceFiles.razor` — drag-drop folder organization, inline rename, breadcrumb | ✅ |
| `WorkspaceController.cs` — `/api/workspace/save-artifact` endpoint for harness write_file callback | ✅ |
| Version history accessible via `ShowVersionHistory()` in context menu | ✅ |
| `EF migrations complete` in startup logs | ✅ |

---

## What Needs to Happen Before PASS

**fred-dev task definition must be updated to reference `fait-v2-agent-harness:38`** (image `62f6a82c`).

However: Even harness:38 (`62f6a82c`, pushed 01:17am May 17) may be missing some Epic 7 harness commits that were added today between 13:08–16:54 EDT. **The harness CI pipeline needs to rebuild from the current HEAD** (`67bd5405`) and register a new task def.

**Required Epic 7 harness commits NOT in `62f6a82c`:**
- `aab17788` — 7.6 per-turn model selection  
- `70241fda`, `0f6f70aa`, `62823204` — 7.4 write_file  
- `6f8238bb` — 7.7 tool manifest + structured system prompt  
- `451a68f0`, `67bd5405` — 7.3 pgvector  
- `e9b5132f`, `9fca311b` — 7.2 CC KB injection  
- `78ca3f1c` — 7.1 KB flags (this is in Blazor, not harness — already deployed)

**`62f6a82c` only contains up to commit `62f6a82c` (fix: add CLAUDE_CODE_USE_BEDROCK=1, 01:17am)** — none of the Epic 7 features were committed at that time.

**Action required: Rebuild harness from HEAD, register new task def, update fred-dev task def to reference it, force-new-deployment.**

---

## Regressions

None detected. Blazor startup clean. ECS healthy. Existing harness sessions (spawned before Epic 7) continue operating normally on their original task def revisions.

---

## Recommendation

**⚠️ PARTIAL PASS — Blazor/backend Epic 7 changes are correctly deployed. Harness Epic 7 features are not deployed.**

Features that work today (Blazor-only):
- 7.5 File Explorer UI (visual, untestable due to Cloudflare)
- 7.1 KB flags in scheduled tasks (code path verified)
- 7.6 Model selector UI (selector renders and saves model, but harness won't honor it)

Features that do NOT work today (harness-dependent):
- 7.4 write_file — tool does not exist in running harness
- 7.7 Tool manifest / behavior quality — not deployed
- 7.3 pgvector — not deployed  
- 7.2 KB CC path — not deployed
- 7.6 Model selection end-to-end — harness side missing

**Do not mark Epic 7 Done. Rebuild and deploy harness from HEAD (`67bd5405`).**
