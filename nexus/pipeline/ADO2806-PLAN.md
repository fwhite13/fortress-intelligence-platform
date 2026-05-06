# BUILD Assignment: ADO#2806
## Wire approved ArtifactGenSystem + TcScanSystem prompts into appsettings.Production.json

**WI:** ADO#2806 | Project: Fortress | Feature: #2797 | Epic: #2793
**Risk:** low | **Pipeline path:** shortcut (config-only, no code, no Clint, no Rhodey needed — BUT does need a nexus-web redeploy to pick up the new appsettings)
**ADO attribution prefix:** `**[Tony Stark — BUILD cycle 1]**`

---

## What to Do

Replace the `Nexus:Prompts:ArtifactGenSystem` value in `appsettings.Production.json` with the v7 prompt from §11 of the spec.

The `TcScanSystem` prompt is already correct in `appsettings.Production.json` — do NOT change it.

---

## Source

**Spec file:** `/home/fredw/.openclaw/workspace/memory/projects/nexus-decomp-upgrade-spec-2026-04-27.md`

The replacement `ArtifactGenSystem` value is the JSON string starting with:
> "You are a technical project manager decomposing an approved software specification into Azure DevOps Agile work items for the Fortress Intelligence Platform..."

It is inside the ```json code block at §11 under the key `"ArtifactGenSystem"`.

---

## Target File

```
/home/fredw/projects/fip/nexus/src/FortressNexus.Web/appsettings.Production.json
```

Replace the value of `Nexus.Prompts.ArtifactGenSystem` with the §11 string. Preserve the `TcScanSystem` value exactly as-is.

---

## Verification

After editing, confirm:
1. `dotnet build` passes (no syntax errors in the JSON file)
2. `grep -c "Fortress Intelligence Platform" appsettings.Production.json` returns 1 (confirms new prompt is in place — old prompt doesn't have this phrase)

---

## Build Report

```markdown
# Build Report — ADO#2806
## CC Invocation
`cat brief.md | claude --model sonnet --print --dangerously-skip-permissions`
## Changes
- `appsettings.Production.json` — replaced ArtifactGenSystem with §11 v7 prompt
## Verification
- dotnet build: PASS
- grep "Fortress Intelligence Platform": FOUND (confirms new prompt)
- TcScanSystem: unchanged (confirmed)
```

---

## ADO Comment

```bash
mcporter call devops.add_comment project=Fortress id=2806 text="**[Tony Stark — BUILD cycle 1]**\nCommit {hash}: appsettings.Production.json — ArtifactGenSystem replaced with §11 v7 prompt. TcScanSystem unchanged. Build: SUCCEEDED."
```

---

## MANDATORY: CC

```bash
export CLAUDE_CODE_ENTRYPOINT=ado-pipeline
export CLAUDE_CODE_DISABLE_AUTO_MEMORY=1
export CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1
export CLAUDE_CODE_GLOB_TIMEOUT_SECONDS=30

cat brief.md | claude --model sonnet --print --dangerously-skip-permissions
```

Working directory: `/home/fredw/projects/fip/nexus/`
Commit message: `config(ADO#2806): wire v7 ArtifactGenSystem prompt into appsettings.Production.json`
