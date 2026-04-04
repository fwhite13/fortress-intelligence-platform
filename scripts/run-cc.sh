#!/usr/bin/env bash
# run-cc.sh — CC pipeline wrapper
# Sets required env vars for all Tony/Clint/Rhodey CC invocations.
# Usage: cat brief.md | ./scripts/run-cc.sh [-- extra claude args]
# Or:    ./scripts/run-cc.sh --model opus < brief.md

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
EXTRA_ARGS=()
MODEL_ARGS=(--model sonnet)  # default

while [[ $# -gt 0 ]]; do
  case "$1" in
    --model)
      if [[ -z "${2:-}" ]]; then
        echo "Error: --model requires an argument" >&2
        exit 1
      fi
      MODEL_ARGS=(--model "$2")
      shift 2
      ;;
    --) shift; EXTRA_ARGS+=("$@"); break ;;
    *) EXTRA_ARGS+=("$1"); shift ;;
  esac
done

# Build command
CMD=(claude "${MODEL_ARGS[@]}" --print --dangerously-skip-permissions)
# --bare not supported in current claude version; CLAUDE_CODE_DISABLE_AUTO_MEMORY=1 covers the non-interactive use case
CMD+=("${EXTRA_ARGS[@]}")

exec "${CMD[@]}"
