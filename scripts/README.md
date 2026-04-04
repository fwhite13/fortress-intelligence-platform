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

# Different model:
cat brief.md | ./scripts/run-cc.sh --model opus
```

### Bare mode

Not available in current claude version. `CLAUDE_CODE_DISABLE_AUTO_MEMORY=1` is set automatically by this wrapper for all invocations.
