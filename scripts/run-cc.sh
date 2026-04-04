#!/usr/bin/env bash
# run-cc.sh — CC pipeline wrapper
# Sets required env vars for all Tony/Clint/Rhodey CC invocations.
# Usage: cat brief.md | ./scripts/run-cc.sh [--bare] [-- extra claude args]
# Or:    ./scripts/run-cc.sh --bare --model opus < brief.md

set -euo pipefail

# Required pipeline env vars
export CLAUDE_CODE_ENTRYPOINT=ado-pipeline
export CLAUDE_CODE_DISABLE_AUTO_MEMORY=1
export CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1
export CLAUDE_CODE_GLOB_TIMEOUT_SECONDS=30

# Optional — set if not already in environment
export CLAUDE_CODE_MAX_OUTPUT_TOKENS="${CLAUDE_CODE_MAX_OUTPUT_TOKENS:-8192}"

# Bedrock (already set in pipeline env, but ensure)
export CLAUDE_CODE_USE_BEDROCK="${CLAUDE_CODE_USE_BEDROCK:-1}"
export AWS_DEFAULT_REGION="${AWS_DEFAULT_REGION:-us-east-1}"

# Parse args
BARE_MODE=0
EXTRA_ARGS=()
MODEL_ARGS=(--model sonnet)  # default

while [[ $# -gt 0 ]]; do
  case "$1" in
    --bare) BARE_MODE=1; shift ;;
    --model) MODEL_ARGS=(--model "$2"); shift 2 ;;
    --) shift; EXTRA_ARGS+=("$@"); break ;;
    *) EXTRA_ARGS+=("$1"); shift ;;
  esac
done

# Build command
CMD=(claude "${MODEL_ARGS[@]}" --print --dangerously-skip-permissions)
if [[ $BARE_MODE -eq 1 ]]; then
  CMD+=(--bare)
fi
CMD+=("${EXTRA_ARGS[@]}")

exec "${CMD[@]}"
