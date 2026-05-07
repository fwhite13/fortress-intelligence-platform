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

---

## ecs-register-task-def.sh — Safe ECS Task Definition Registration

Wraps `aws ecs register-task-definition` with a `taskRoleArn` inheritance safeguard. Prevents silent IAM permission failures caused by `taskRoleArn` being omitted from a new task def JSON.

### What it does:
1. Fetches the currently-registered task def ARN from the ECS service
2. Extracts `taskRoleArn` from that revision
3. Checks if `taskRoleArn` is present in the new task def JSON
4. If missing: injects it from the current revision (logs the inherited ARN)
5. If present: confirms it and proceeds unchanged
6. If both are missing: logs a warning and proceeds (does not block)
7. Writes the (possibly-patched) JSON to a temp file and calls `register-task-definition`
8. Outputs the new task def ARN on success

### When to use:
Any time a deploy brief includes `aws ecs register-task-definition`. Reference this script in Rhodey deploy briefs instead of calling the AWS CLI directly.

### Usage:
```bash
./scripts/ecs-register-task-def.sh \
  --cluster fortress-tools-cluster \
  --service fait-prod \
  --task-def-json /tmp/td-new.json \
  [--region us-east-1] \
  [--profile fortress-tools-deployer]
```

### Requirements:
- `jq` must be installed
- AWS credentials must be configured (via `--profile` or environment)
