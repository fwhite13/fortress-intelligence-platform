# BUILD Assignment: FAIT Improvements June 11 — Continuation
**Sprint:** FAIT Improvements June 11
**Date:** 2026-06-11
**WIs remaining:** #5114, #5115, #5116 (WIs #5111, #5112, #5113 already complete and committed)

---

## MANDATORY: New Pipeline Standard This Sprint

- Use `--output-format stream-json` on ALL CC invocations (no exceptions)
- Every WI has a `goalCondition` — use it verbatim as the `/goal` clause in CC
- If CC exits WITHOUT meeting the goalCondition: write a Goal Failure Brief and report back to Maria immediately. Do NOT continue to the next WI.

**Goal Failure Brief format:**
```
## Goal Failure — WI #XXXX
goalCondition: [exact condition from ADO]
last_eval_reason: [what CC reported as the reason goal was not met]
turns_used: N
what_was_completed: [describe any partial progress]
```

---

## CC Invocation Standard

```bash
export CLAUDE_CODE_ENTRYPOINT=ado-pipeline
export CLAUDE_CODE_DISABLE_AUTO_MEMORY=1
export CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1
export CLAUDE_CODE_GLOB_TIMEOUT_SECONDS=30

cat brief-WIXXXX.md | claude \
  --model sonnet \
  --output-format stream-json \
  --print \
  --dangerously-skip-permissions
```

Include the `/goal <goalCondition>` at the TOP of the brief file (before the task description).

---

## Working Directory
- FAIT project root: `/home/fredw/projects/fip/`
- Blazor Server (FAIT app): `/home/fredw/projects/fip/fait/src/`
- Agent Harness (Node.js): `/home/fredw/projects/fip/fait/agent-harness/`
- Workspace template: `/home/fredw/projects/fip/fait/agent-harness/workspace-template/`
- Harness CLAUDE.md: `/home/fredw/projects/fip/fait/agent-harness/workspace-template/.claude/CLAUDE.md`
- Pipeline artifacts: `/home/fredw/projects/fip/fait/pipeline/`

---

## WI #5114 — Ephemeral chips: three remaining UX defects

**Type:** Bug
**Title:** FAIT ephemeral chips — three remaining issues: descriptions are raw bash/technical, position above assistant message, vertical squeeze at bottom of chat

### Defect 1: Chip descriptions show raw bash commands
Chips show actual bash commands (e.g. `python3 generate_report.py --format xlsx`) instead of user-friendly summaries (e.g. "Generating Excel report").

**Fix:** The harness layer that constructs `task_progress` chip descriptions needs a mapping/summarization step before emitting. Use a lookup table of known tool patterns → friendly labels:
- `python3 *generate*` or `*xlsx*` → "Generating Excel report"
- `python3 *create*` → "Creating file"
- `python3 *` (generic) → "Running Python script"
- `pip install *` → "Installing Python package"
- `cat *.md` / file read patterns → "Reading document"
- `ls *` / `find *` → "Scanning files"
- Write/output operations → "Writing output file"
- Default fallback → "Processing..."

### Defect 2: Chips appear above assistant message instead of below
Fix rendering order so chips render directly under the assistant message they belong to.

### Defect 3: Chips get vertically squeezed near bottom of screen
The chip container must have a fixed/min-height and must NOT inherit viewport height constraints that shrink it as the page fills.

**Acceptance Criteria:**
1. Chip descriptions show business-user-friendly text — no raw bash commands
2. Chips appear below the assistant message, not above
3. Chips maintain consistent full height throughout conversation regardless of scroll position
4. All three verified in fred-dev

**goalCondition (verbatim — use as /goal clause):**
`Chip descriptions show plain-English summaries (no bash commands or raw tool output); chips render below the assistant message text; chips maintain consistent height regardless of scroll position throughout a multi-step CC task, or stop after 25 turns`

**Build Report:** `/home/fredw/projects/fip/fait/pipeline/ADO5114-BUILD-REPORT.md`

---

## WI #5115 — Excel pivot table limitation: update CLAUDE.md

**Type:** Bug
**Title:** FAIT CC cannot generate real Excel pivot tables — openpyxl/xlsxwriter do not support native pivot table XML

**Problem:** CC silently creates a manual summary/aggregation table and calls it a pivot table. Neither openpyxl nor xlsxwriter supports native Excel pivot table XML generation (no PivotCache, no PivotField, no interactive refresh).

**Fix:** Update CLAUDE.md in the harness workspace template to document this limitation honestly.

**File to update:** `/home/fredw/projects/fip/fait/agent-harness/workspace-template/.claude/CLAUDE.md`

**What to add:**
- A section documenting the pivot table limitation
- CC should:
  - Acknowledge the limitation explicitly to the user when a pivot table is requested
  - Generate a structured summary/aggregation table with formulas as the alternative
  - Label it clearly (e.g., "Summary Table — Note: Excel's interactive pivot table feature is not supported by available Python libraries. This is a structured summary table with equivalent data.")
  - Never claim it has created an interactive pivot table

**Acceptance Criteria:**
1. Root cause documented in CLAUDE.md
2. CLAUDE.md has clear guidance for CC on handling pivot table requests
3. No more silent claims of having created a pivot table

**goalCondition (verbatim — use as /goal clause):**
`Either: a real Excel pivot table (with PivotCache XML) is generated programmatically and opens as an interactive pivot table in Excel, OR CLAUDE.md is updated to document the limitation and CC generates a clearly-labeled summary table instead with no claim of pivot functionality, or stop after 20 turns`

**Build Report:** `/home/fredw/projects/fip/fait/pipeline/ADO5115-BUILD-REPORT.md`

---

## WI #5116 — FAIT harness: implement CC Dynamic Workflows and /goal

**Type:** User Story
**Title:** FAIT harness: implement CC Dynamic Workflows and /goal for long-running tasks

**CRITICAL: Read the full spec FIRST:**
```
/home/fredw/.openclaw/workspace/memory/projects/fait-harness-cc-new-features-spec.md
```

This is the largest WI in the sprint. The spec has full architecture, sequence diagrams, and implementation notes. Read all of it before writing any code.

### Scope (5 items — implement in this order):

**1. stream-json streaming pipeline (start here — most architectural risk)**
- Switch harness CC invocation to `--output-format stream-json`
- Parse CC stdout events line-by-line — NO buffering
- Emit corresponding `HarnessEvent` SSE to Blazor as events arrive
- Map event types per spec Section 1 table

**2. /goal wiring**
- Add optional `completionCondition` field to `TurnRequest`
- When set, harness prepends `/goal <condition>` to the CC invocation (as first line of prompt)
- Always include `or stop after N turns` clause
- See spec Section 2 for exact construction pattern

**3. Dynamic workflow support**
- Wire harness to support CC-spawned subagents in a workflow context
- Verify tool allowlist in `.claude/settings.json` workspace template permits workflow pattern
- See spec Section 3

**4. Workspace template update**
Update `/home/fredw/projects/fip/fait/agent-harness/workspace-template/.claude/settings.json`:
```json
{
  "model": "claude-sonnet-4-6",
  "enabledPlugins": {
    "security-guidance@claude-plugins-official": true
  },
  "permissions": {
    "allow": [
      "Read",
      "Write(/workspace/**)",
      "Bash(python3 *)",
      "Bash(pip install *)"
    ],
    "deny": [
      "Bash(curl *)",
      "Bash(wget *)",
      "Bash(ssh *)",
      "mcp__network__*"
    ]
  }
}
```

**5. Blazor progress UI**
- Extend `CCProgressHub` (SignalR) to surface `workflow_phase`, `goal_eval`, `tool_use` events to browser
- User sees: live phase name, current tool call description, elapsed time
- See spec Section 1 table for full event type → display mapping

**Model IDs (ONLY these two):**
- Default: `us.anthropic.claude-sonnet-4-6`
- Lightweight: `us.anthropic.claude-haiku-4-5-20251001-v1:0`

**Acceptance Criteria:**
1. CC tasks run with `--output-format stream-json`; harness streams events to Blazor without buffering
2. `completionCondition` field on TurnRequest wires to `/goal` in CC invocation
3. Blazor progress UI shows live workflow phase, current tool description, elapsed time during long-running tasks
4. Workspace template `.claude/settings.json` updated with correct permissions
5. Tom use case (multi-doc analysis → PPTX) runs end-to-end with live progress visible
6. Verified in fred-dev

**goalCondition (verbatim — use as /goal clause):**
`Harness invokes CC with --output-format stream-json; TurnRequest accepts completionCondition field and CC is invoked with /goal when set; Blazor progress UI shows live workflow phase and tool description during a running task; Tom use-case (multi-doc folder → PPTX) runs end-to-end with visible progress, or stop after 40 turns`

**Build Report:** `/home/fredw/projects/fip/fait/pipeline/ADO5116-BUILD-REPORT.md`

---

## Key Architecture Reminders

- **Blazor + JSInterop:** Never pass `byte[]` directly — always `Convert.ToBase64String`
- **Blazor deadlock pattern:** Never `await dialog.Result` inside `await foreach` SSE loop — use `ContinueWith` + `break`
- **AWS credentials:** Use `fortress-tools-deployer` for AWS ops
- **ECS cluster:** `fortress-tools-cluster`

---

## When Done

Report back with:
1. Status per WI (BUILT / GOAL FAILURE)
2. All three Build Reports written
3. ADO comments posted for each WI
4. Any blockers encountered

If any WI hits a goal failure: stop immediately, write the failure brief, and report back. Do not continue.
