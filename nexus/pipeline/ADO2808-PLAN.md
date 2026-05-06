# BUILD Assignment: ADO#2808
## NEXUS ArtifactGen: Validate two-call TC architecture via v7 validation script

**WI:** ADO#2808 | Project: Fortress | Feature: #2797 | Epic: #2793
**Risk:** low | **Path:** shortcut (no code changes, no deploy, no Clint, no Rhodey)
**ADO attribution prefix for all comments:** `**[Tony Stark — BUILD cycle 1]**`

---

## What This Is

Validate the two-call TC architecture currently deployed in NEXUS against the §G 13-item checklist. This is a standalone Python script run — no code changes to the NEXUS codebase.

The deployed architecture (from ADO#2585):
- **Call 1:** `ArtifactGenSystem` prompt → decomposition → returns JSON WI array
- **Call 2:** `TcScanSystem` prompt → TC compliance scan → returns `{testCases, parentUpdates}` JSON
- Call 2 is merged into Call 1's output

Previous scores: v1=7/13, v2=3/13, v3=6/13, v4=8/13, v5=10/13, v6=11/13

---

## Task

Create and run `/home/fredw/projects/fip/nexus/pipeline/run_v7_validation.py`.

This script replicates the exact two-call architecture from `ArtifactGenerationService.cs` using the **currently deployed prompts** from `appsettings.Production.json`, then scores the result against the §G checklist.

---

## Step 1: Read the deployed prompts

The prompts are already in `appsettings.Production.json`:
```
/home/fredw/projects/fip/nexus/src/FortressNexus.Web/appsettings.Production.json
```

Read keys:
- `Nexus.Prompts.ArtifactGenSystem` → Call 1 system prompt
- `Nexus.Prompts.TcScanSystem` → Call 2 system prompt

These are JSON strings with `\n` escapes. Parse them as JSON strings (Python's `json.loads(f'"{value}"')` approach, or load the whole JSON file and navigate the path).

**Do NOT use a prompt candidate .md file** — use the deployed appsettings prompts directly.

---

## Step 2: Replicate the two-call architecture

Mirror `ArtifactGenerationService.GenerateWorkItemsAsync()` exactly:

**Call 1 — Decomposition:**
```python
# System: ArtifactGenSystem prompt
# User: "Please decompose the following specification into Azure DevOps work items per your instructions:\n\n---\n\n" + forge_spec
# Model: us.anthropic.claude-sonnet-4-20250514-v1:0
# max_tokens: 32768
# beta: ["output-128k-2025-02-19"]
```

Parse Call 1 output as JSON array → `items` list.

**WiClassifier post-processing (mirror the service):**
For each item in `items`, classify `wiTemplate` from the title/description:
- Check title for infra keywords (`create ecr`, `ecr repo`, `iam role`, `ecs service`, `alb`, etc.) → `"infrastructure"`
- Check title for migration keywords (`migrate`, `migration`, `replace`, `move from`, etc.) → `"migration"`
- Otherwise → `"standard"` (or keep whatever the model set)
- Set `isExternalDependency = True` if tags contain `blocked-external`

(The service also calls `IWiClassifier.ClassifyStory()` — replicate its logic inline.)

**Call 2 — TC Compliance Scan:**
```python
# System: TcScanSystem prompt
# User: f"WORK ITEM ARRAY:\n{json.dumps(items)}\n\nORIGINAL SPEC:\n{forge_spec}"
# Model: same
# max_tokens: 32768
```

Parse Call 2 output as JSON object `{testCases: [...], parentUpdates: [...]}`.
Merge: `items.extend(tc_result["testCases"])`
Apply parentUpdates to add `testedByTitles` to parent stories (optional for scoring).

**If Call 2 fails:** log the error, continue scoring with Call 1 output only (non-fatal per the service).

---

## Step 3: Score §G checklist

Run the same §G scoring logic from `run_v6_validation.py` but on the merged output. The §G checklist is:

| # | Check |
|---|-------|
| G1 | Infra WIs have `wiTemplate = "infrastructure"` |
| G2 | Ext dep WIs have `blocked-external` + `owner-*` tags |
| G3 | All external owners extracted from spec found |
| G4 | No duplicate ext dep WIs per owner |
| G5 | Open questions consolidated (1 WI per external owner) |
| G6 | TC Rule A fires (security keyword stories have ≥1 TC) |
| G7 | TC Rule B fires (stories with 4+ ACs have ≥1 TC) |
| G8 | Separate Epic for separate app DB work |
| G9 | Prerequisite schema work tracked in ADO |
| G10 | Follow-on migration WI exists (incl. conditional/deferred) |
| G11 | Every User Story has `specReference` (non-null, has §N) |
| G12 | Every TC has `rationale` citing a spec section |
| G13 | Every User Story has ≥2 Task children |

Use the FORGE KB spec as input:
```
/home/fredw/.openclaw/workspace/memory/projects/forge-kb-mcp-server-spec-2026-04-27.md
```

---

## Step 4: Write output files

**JSON output:** `/home/fredw/projects/fip/nexus/pipeline/ADO2808-BEDROCK-OUTPUT.json`
- Write the full merged WI array (Call 1 + Call 2 TCs)

**Build Report:** `/home/fredw/projects/fip/nexus/pipeline/ADO2808-BUILD-REPORT.md`

Build report must include:
- Model used, token counts for both calls
- WI type counts table
- §G checklist table (# | Check | Result | Notes)
- **Score: N/13**
- For each FAIL: deep dive explaining exactly which items failed and why
- Run history table: v1=7/13 (ADO#2531), v2=3/13 (ADO#2543), v3=6/13 (ADO#2555), v4=8/13 (ADO#2558), v5=10/13 (ADO#2577), v6=11/13 (ADO#2581), v7=?/13 (ADO#2808)

---

## Step 5: ADO comment

After completing, post this comment to ADO#2808 (project: Fortress):

```
**[Tony Stark — BUILD cycle 1]**
run_v7_validation.py complete. Score: [N]/13. Call 1: [X] tokens. Call 2: [Y] tokens. Output: ADO2808-BUILD-REPORT.md.
```

Use:
```bash
mcporter call devops.add_comment project=Fortress id=2808 text="**[Tony Stark — BUILD cycle 1]**\nrun_v7_validation.py complete. Score: [N]/13. ..."
```

---

## CC Env Vars (mandatory)

```bash
export CLAUDE_CODE_ENTRYPOINT=ado-pipeline
export CLAUDE_CODE_DISABLE_AUTO_MEMORY=1
export CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1
export CLAUDE_CODE_GLOB_TIMEOUT_SECONDS=30
```

Working directory: `/home/fredw/projects/fip/nexus/`

---

## Reference: v6 script

The v6 script is at `/home/fredw/projects/fip/nexus/pipeline/run_v6_validation.py` (212 lines). It uses a single Bedrock call. The v7 script adds:
1. Reading prompts from appsettings.Production.json instead of a .md candidate file
2. Two-call architecture (Call 1 decomp → Call 2 TC scan)
3. Merging the TC scan results into the final array

The §G scoring logic from v6 can be reused largely as-is for G1-G5, G8-G13. G6 and G7 should now PASS because Call 2 explicitly handles TC generation.

---

## Deliverables

1. `run_v7_validation.py` created and executed
2. `ADO2808-BEDROCK-OUTPUT.json` — merged WI array
3. `ADO2808-BUILD-REPORT.md` — score + §G breakdown
4. ADO#2808 comment posted

No code changes to nexus-web. No deploy. No Clint. No Rhodey.
