# Pipeline Scripts

## run-cc.sh — CC Pipeline Wrapper

Sets required env vars for all Tony/Clint/Rhodey CC invocations.

### Required env vars set automatically:
- `CLAUDE_CODE_ENTRYPOINT=ado-pipeline` — billing attribution
- `CLAUDE_CODE_DISABLE_AUTO_MEMORY=1` — no cross-run memory accumulation
- `CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1` — prevents CWD drift between bash calls
- `CLAUDE_CODE_GLOB_TIMEOUT_SECONDS=30` — prevents glob hangs on large repos

### Optional env vars (set if not already in environment):
- `CLAUDE_CODE_MAX_OUTPUT_TOKENS=8192` — output token cap

### Usage:

```bash
# Standard (full mode, model sonnet):
cat brief.md | ./scripts/run-cc.sh

# Bare mode (pure code tasks — no CLAUDE.md, no memory):
cat brief.md | ./scripts/run-cc.sh --bare

# Different model:
cat brief.md | ./scripts/run-cc.sh --model opus

# Combined:
cat brief.md | ./scripts/run-cc.sh --bare --model opus
```

### When to use --bare:
- Tony/Clint pure implementation tasks where the WI is fully self-contained
- DO NOT use for Rhodey (needs project conventions)
- DO NOT use for tasks that reference prior decisions or expect project context
