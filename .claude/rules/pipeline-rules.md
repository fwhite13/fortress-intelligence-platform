# Pipeline Rules

## CC Invocation

All pipeline CC invocations use `scripts/run-cc.sh` (never call `claude` directly).

```bash
# Standard:
cat brief.md | ./scripts/run-cc.sh

# Bare mode (pure code tasks):
cat brief.md | ./scripts/run-cc.sh --bare
```

## When to Use --bare Mode

Use `--bare` for Tony/Clint pure implementation tasks where:
- The WI is fully self-contained (no need for project conventions from CLAUDE.md)
- No reference to prior decisions or accumulated context needed

Do NOT use `--bare` for:
- Rhodey (needs project conventions for deploy targets, credential names)
- Tasks referencing architectural decisions
- Multi-file refactors where context matters

## Required Env Vars

Set automatically by `run-cc.sh`. For reference:

| Var | Value | Why |
|-----|-------|-----|
| `CLAUDE_CODE_ENTRYPOINT` | `ado-pipeline` | Billing attribution |
| `CLAUDE_CODE_DISABLE_AUTO_MEMORY` | `1` | No cross-run memory accumulation in CI |
| `CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR` | `1` | Prevent CWD drift between bash calls |
| `CLAUDE_CODE_GLOB_TIMEOUT_SECONDS` | `30` | Prevent glob hangs on large repos |

## ANTHROPIC_API_KEY / Bedrock

Pipeline agents use Bedrock. `CLAUDE_CODE_USE_BEDROCK=1` and `AWS_DEFAULT_REGION=us-east-1` must be set (handled by `run-cc.sh` with fallback defaults). If switching to direct Anthropic API: set `ANTHROPIC_API_KEY` or CC will hang on keychain read at startup.
