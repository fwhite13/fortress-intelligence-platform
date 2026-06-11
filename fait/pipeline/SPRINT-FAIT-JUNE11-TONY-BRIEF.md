# BUILD Assignment: FAIT Improvements June 11
**Sprint:** FAIT Improvements June 11
**Registry ID:** pipeline-fait-improvements-june11-20260611
**Date:** 2026-06-11
**WIs:** #5111, #5112, #5113, #5114, #5115, #5116

---

## MANDATORY: New Pipeline Standard This Sprint

- Use `--output-format stream-json` on ALL CC invocations (no exceptions)
- Every WI has a `goalCondition` in its ADO description — use it verbatim as the `/goal` clause in CC
- If CC exits WITHOUT meeting the goalCondition: write a Goal Failure Brief and report back to Maria immediately. Do NOT continue to the next WI.
- Goal Failure Brief format:
  ```
  ## Goal Failure — WI #XXXX
  goalCondition: [exact condition from ADO]
  last_eval_reason: [what CC reported as the reason goal was not met]
  turns_used: N
  what_was_completed: [describe any partial progress]
  ```

---

## CC Invocation Standard

All CC invocations must follow this pattern:

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

Include the `/goal <goalCondition>` at the TOP of the brief file (before the task description), verbatim from the ADO WI's goalCondition field.

---

## Working Directory

All FAIT code lives at: `/home/fredw/projects/fip/`

- Blazor Server (FAIT app): `/home/fredw/projects/fip/fait/src/`
- Agent Harness (Node.js): `/home/fredw/projects/fip/fait/agent-harness/`
- Workspace template: `/home/fredw/projects/fip/fait/agent-harness/workspace-template/`
- Harness CLAUDE.md: `/home/fredw/projects/fip/fait/agent-harness/workspace-template/.claude/CLAUDE.md`
- Pipeline artifacts output: `/home/fredw/projects/fip/fait/pipeline/`

---

## WI #5111 — Cache key includes artifact version/record ID

**Type:** Bug  
**Title:** FAIT file preview cache not invalidated on new file version — same filename, new version shows stale preview

**Problem:**  
The file preview cache does not account for file versioning. When a new version of a file is uploaded (same filename, new version ID in DB), the cached preview from the previous version is served instead of regenerating.

**Root cause hypothesis:** Cache key is keyed on filename or artifact S3 path, not on version ID. When a new version is created, the key matches the old entry → stale preview returned.

**Fix scope:** Wherever the preview cache key is constructed (likely in the Blazor artifact sidebar or the preview generation service) — change the cache key to incorporate the artifact version/record ID, not just filename or path.

**Acceptance Criteria:**
1. Upload file → preview generates. Upload new version of same file → preview regenerates (not served from cache).
2. Cache still works for repeated views of the same version (no unnecessary regeneration).
3. Fix verified in fred-dev.

**goalCondition (verbatim — use as /goal clause):**  
`Cache key includes artifact version/record ID; uploading a new version of an existing file triggers preview regeneration (not served from cache); repeated views of the same version do not regenerate, or stop after 20 turns`

**Implementation guidance:**
- Find the preview cache implementation — search for MemoryCache, IMemoryCache, or any caching layer in the Blazor artifact/preview code
- The cache key must include the artifact DB record ID or version number, not just filename/path
- Typical pattern: `$"preview_{artifactId}_{versionId}"` or similar
- Do NOT regenerate on every view — only on new version uploads

---

## WI #5112 — CC task continues after working folder modal closes

**Type:** Bug  
**Title:** FAIT CC task stops immediately after working folder selector modal closes — regression from modal persistence fix

**Problem:**  
After selecting a working folder in the task modal, the modal closes and the CC task stops executing immediately. This is a regression from a previous fix that addressed the modal staying open for the entire task duration.

**Root cause hypothesis:**  
The previous fix that dismissed the modal on folder selection also interrupted/dropped the task continuation signal. The modal close event may be incorrectly triggering a task abort, or the harness task context is being torn down when the modal dismisses.

**Investigation required:**
1. Pull harness logs for affected chat sessions — look for lifecycle events (task_start, task_hold, done, error) correlating with modal close timestamp
2. The harness should be in a waiting/held state while modal is open — check if held state is correctly resumed or aborted on modal dismiss
3. See MEMORY.md entry on "Blazor Server: `await dialog.Result` inside `await foreach` SSE loop = deadlock" — this may be related; the fix pattern is break + ContinueWith

**Acceptance Criteria:**
1. User selects working folder → modal closes → task continues executing without interruption.
2. No manual re-prompt required after folder selection.
3. Harness logs show task_hold → folder selected → task resumes (no premature done/error).
4. Verified in fred-dev.

**goalCondition (verbatim — use as /goal clause):**  
`Harness logs show task_hold → folder_selected → task_resumes with no done/error emitted in between, and a full CC task completes without user re-prompt after folder selection, or stop after 20 turns`

**CRITICAL — Blazor Deadlock Pattern:**  
Per MEMORY.md: Never `await dialog.Result` inside an `await foreach` event loop on Blazor Server. Use `ContinueWith` + `break` to free the circuit:
```csharp
// Break SSE loop — harness is paused, no more events coming
_ = dialog.Result.ContinueWith(async resultTask => {
    var result = resultTask.IsCompletedSuccessfully ? resultTask.Result : null;
    if (result != null && !result.Canceled) {
        await InvokeAsync(() => { /* handle confirm */ });
    } else {
        await InvokeAsync(() => { /* handle cancel, clear state */ });
    }
});
break; // safe — harness is waiting for /turn/folder-confirm
```

---

## WI #5113 — XLSX preview generation fix

**Type:** Bug  
**Title:** FAIT XLSX preview generation failing — investigate logs for two failed conversions

**Problem:**  
Preview generation for XLSX files created during a CC task failed for two separate files. Both returned "Preview conversion failed. Try downloading the file." error in the UI.

**Investigation steps:**
1. Pull CloudWatch/ECS logs for the LibreOffice conversion service (ADO#4620) around 2026-06-10 Fred test session
2. Check if the XLSX files had charts or pivot tables that broke LibreOffice conversion
3. Verify ADO#5021 (XLSX preview via LibreOffice) is deployed to fred-dev and feature flag is on
4. Check conversion service health and confirm XLSX → PDF → preview rendering path is end-to-end functional

**AWS context:**
- ECS cluster: `fortress-tools-cluster`
- Use `fortress-tools-deployer` credentials for all AWS operations
- Health check URL: `https://fait.dev.fortressam.ai/health`

**Acceptance Criteria:**
1. Root cause of conversion failure identified from logs.
2. Fix applied — CC-generated XLSX files (including multi-sheet with charts) preview successfully in fred-dev.
3. Error message path remains for genuinely unrenderable files.

**goalCondition (verbatim — use as /goal clause):**  
`Root cause identified from CloudWatch logs, fix applied, and a CC-generated XLSX file (multi-sheet with charts) renders a preview successfully in fred-dev without a conversion error, or stop after 25 turns`

---

## WI #5114 — Ephemeral chips UX: three remaining defects

**Type:** Bug  
**Title:** FAIT ephemeral chips — three remaining issues: descriptions are raw bash/technical, position above assistant message, vertical squeeze at bottom of chat

**Three defects:**

### 1. Chip descriptions are raw technical content
Chips show actual bash commands (e.g. `python3 generate_report.py --format xlsx`) instead of user-friendly labels (e.g. "Generating Excel report").

**Fix:** The harness layer that constructs `task_progress` chip descriptions must apply a mapping/summarization step. Use a lookup table of known tool patterns → friendly labels. Examples:
- `python3 *.py` → "Running Python script"
- `python3 *generate*.py` → "Generating report"
- `python3 *xlsx*.py` → "Creating Excel file"
- `pip install *` → "Installing Python package"
- `cat *.md` → "Reading document"
- `ls *` / `find *` → "Scanning files"
- Bash commands involving file write operations → "Writing output file"

### 2. Chips positioned above assistant message instead of below
Fix the rendering order — chips must appear directly under the assistant message they belong to, not above.

### 3. Chips get vertically squeezed near bottom of screen
The chip container must have a fixed/min-height and must NOT inherit viewport height constraints that shrink it as the chat scrolls toward the bottom.

**Acceptance Criteria:**
1. Chip descriptions show business-user-friendly text — no raw bash commands.
2. Chips appear below the assistant message, not above.
3. Chips maintain consistent full height throughout conversation regardless of scroll position.
4. All three verified in fred-dev across a multi-step CC task.

**goalCondition (verbatim — use as /goal clause):**  
`Chip descriptions show plain-English summaries (no bash commands or raw tool output); chips render below the assistant message text; chips maintain consistent height regardless of scroll position throughout a multi-step CC task, or stop after 25 turns`

---

## WI #5115 — Excel pivot table limitation — honest documentation

**Type:** Bug  
**Title:** FAIT CC cannot generate real Excel pivot tables — openpyxl/xlsxwriter do not support native pivot table XML

**Problem:**  
When CC attempts to create a pivot table, it uses openpyxl or xlsxwriter — neither supports native Excel pivot table generation. CC silently creates a manual summary/aggregation table and calls it a pivot table.

**Options evaluated:**
1. pywin32/COM automation — not viable (requires Windows + Excel, not in Linux Fargate)
2. xlwings — also requires Excel
3. openpyxl raw XML injection — possible but fragile, high maintenance
4. **Recommended: Option 4** — Update CLAUDE.md to document the limitation. CC generates a clearly-labeled summary table with formulas and communicates honestly that it is NOT an interactive pivot table.

**Implementation:**
- Update `/home/fredw/projects/fip/fait/agent-harness/workspace-template/.claude/CLAUDE.md`
- Add a section documenting the pivot table limitation
- Specify that CC should:
  - Acknowledge the limitation explicitly to the user
  - Generate a structured summary/aggregation table with formulas as the alternative
  - Label it clearly: "Summary Table (note: Excel's interactive pivot table feature is not supported by available Python libraries)"
  - Never claim it has created an interactive pivot table

**Acceptance Criteria:**
1. Root cause confirmed in CLAUDE.md documentation.
2. CLAUDE.md updated with honest pivot table limitation documentation.
3. CC communicates accurately to users and delivers clearly-labeled summary tables instead.

**goalCondition (verbatim — use as /goal clause):**  
`Either: a real Excel pivot table (with PivotCache XML) is generated programmatically and opens as an interactive pivot table in Excel, OR CLAUDE.md is updated to document the limitation and CC generates a clearly-labeled summary table instead with no claim of pivot functionality, or stop after 20 turns`

---

## WI #5116 — FAIT harness: implement CC Dynamic Workflows and /goal

**Type:** User Story  
**Title:** FAIT harness: implement CC Dynamic Workflows and /goal for long-running tasks

**CRITICAL:** Read the full spec before starting:
```
/home/fredw/.openclaw/workspace/memory/projects/fait-harness-cc-new-features-spec.md
```

**Scope (5 items):**

### 1. stream-json streaming pipeline
- Switch harness CC invocation to `--output-format stream-json`
- Parse CC stdout events line-by-line
- Emit corresponding `HarnessEvent` SSE to Blazor as events arrive — NO buffering
- Map event types per spec Section 1 table

### 2. /goal wiring
- Add optional `completionCondition` field to `TurnRequest`
- When set, harness prepends `/goal <condition>` to the CC invocation
- Always include `or stop after N turns` clause
- See spec Section 2 for exact construction pattern

### 3. Dynamic workflow support
- Wire harness to support CC-spawned subagents in a workflow context
- Verify tool allowlist in `.claude/settings.json` workspace template permits workflow pattern
- See spec Section 3

### 4. Workspace template update
Update `.claude/settings.json` in harness workspace template:
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

### 5. Blazor progress UI
- Extend `CCProgressHub` (SignalR) to surface `workflow_phase`, `goal_eval`, `tool_use` events to browser
- User sees: live phase name, current tool call description, elapsed time
- See spec Section 1 table for full event type → display mapping

**Model IDs (only these):**
- Default: `us.anthropic.claude-sonnet-4-6`
- Lightweight: `us.anthropic.claude-haiku-4-5-20251001-v1:0`

**Implementation order:**
1. **Start with streaming first** (most architectural risk) — validate stream-json → SignalR pipeline with simple single-agent task
2. Then wire /goal
3. Then workflows + workspace template
4. Then Blazor progress UI

**Acceptance Criteria:**
1. CC tasks run with `--output-format stream-json`; harness streams events to Blazor without buffering.
2. `completionCondition` field on TurnRequest wires to `/goal` in CC invocation.
3. Blazor progress UI shows live workflow phase, current tool description, elapsed time during long-running tasks.
4. Workspace template `.claude/settings.json` updated with correct permissions.
5. Tom use case (multi-doc analysis → PPTX) runs end-to-end with live progress visible.
6. Verified in fred-dev.

**goalCondition (verbatim — use as /goal clause):**  
`Harness invokes CC with --output-format stream-json; TurnRequest accepts completionCondition field and CC is invoked with /goal when set; Blazor progress UI shows live workflow phase and tool description during a running task; Tom use-case (multi-doc folder → PPTX) runs end-to-end with visible progress, or stop after 40 turns`

---

## Deliverables

For each WI, produce:
1. A Build Report: `/home/fredw/projects/fip/fait/pipeline/ADO{N}-BUILD-REPORT.md`
2. ADO comment: post the CC invocation command used + key evidence the goalCondition was met
3. ADO state: confirm the WI is updated with commit reference

### Build Report Format (per WI)
```markdown
## Build Report — ADO#XXXX
**WI:** [title]
**Date:** [date]
**Status:** COMPLETE

### CC Invocation
[exact command used including --output-format stream-json]

### Goal Condition
[verbatim goalCondition from ADO]

### Goal Outcome
ACHIEVED / NOT ACHIEVED (if not achieved, stop and write a failure brief)

### Files Modified
- [list of files changed]

### Changes Made
[description of what was implemented]

### Self-Review Checklist
- [ ] All ACs verified
- [ ] No hardcoded values that should be constants
- [ ] Error handling for edge cases
- [ ] No debug artifacts left behind
- [ ] Consistency Map items verified (if applicable)
```

---

## Key Architecture Reminders

- **Blazor + JSInterop:** Never pass `byte[]` directly — always `Convert.ToBase64String` (see MEMORY.md)
- **Blazor deadlock pattern:** Never `await dialog.Result` inside `await foreach` SSE loop — use `ContinueWith` + `break`
- **AWS credentials:** Use `fortress-tools-deployer` for AWS ops
- **ECS cluster:** `fortress-tools-cluster`
- **fait CodeBuild:** `fip-fait-build`
- **Health check:** `https://fait.dev.fortressam.ai/health`

---

## When Done

Report back to Maria with:
1. Status per WI (BUILT / GOAL FAILURE)
2. All Build Reports written to `/home/fredw/projects/fip/fait/pipeline/ADO{N}-BUILD-REPORT.md`
3. ADO comments posted for each WI
4. Any blockers or surprises encountered

If any WI hits a goal failure: stop immediately, write the failure brief, and report back. Do not continue to the next WI.
