#!/bin/bash
# ecs-register-task-def.sh — Safe ECS task definition registration
# Automatically inherits taskRoleArn from the currently-running revision
# if it's missing from the new task def JSON.
#
# Usage:
#   ./scripts/ecs-register-task-def.sh \
#     --cluster fortress-tools-cluster \
#     --service fait-prod \
#     --task-def-json /tmp/td-new.json \
#     [--region us-east-1] \
#     [--profile fortress-tools-deployer]

set -euo pipefail

# --- Dependency check ---
command -v jq >/dev/null 2>&1 || { echo "[deploy] ERROR: jq is required but not installed."; exit 1; }
command -v aws >/dev/null 2>&1 || { echo "[deploy] ERROR: aws CLI is required but not installed."; exit 1; }

# --- Arg parsing ---
CLUSTER=""
SERVICE=""
TASK_DEF_JSON=""
REGION=""
PROFILE=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --cluster)   CLUSTER="$2";        shift 2 ;;
    --service)   SERVICE="$2";        shift 2 ;;
    --task-def-json) TASK_DEF_JSON="$2"; shift 2 ;;
    --region)    REGION="$2";         shift 2 ;;
    --profile)   PROFILE="$2";        shift 2 ;;
    *) echo "[deploy] ERROR: Unknown argument: $1"; exit 1 ;;
  esac
done

# --- Validate required args ---
if [[ -z "$CLUSTER" || -z "$SERVICE" || -z "$TASK_DEF_JSON" ]]; then
  echo "[deploy] ERROR: --cluster, --service, and --task-def-json are required."
  echo "Usage: $0 --cluster <name> --service <name> --task-def-json <path> [--region <region>] [--profile <profile>]"
  exit 1
fi

if [[ ! -f "$TASK_DEF_JSON" ]]; then
  echo "[deploy] ERROR: Task def JSON file not found: $TASK_DEF_JSON"
  exit 1
fi

# --- Build AWS CLI flags ---
AWS_OPTS=()
[[ -n "$REGION" ]]  && AWS_OPTS+=(--region "$REGION")
[[ -n "$PROFILE" ]] && AWS_OPTS+=(--profile "$PROFILE")

# --- Fetch current task def ARN from the service ---
echo "[deploy] Fetching current task definition from service: $SERVICE (cluster: $CLUSTER)"
CURRENT_TD_ARN=$(aws ecs describe-services \
  "${AWS_OPTS[@]}" \
  --cluster "$CLUSTER" \
  --services "$SERVICE" \
  --query 'services[0].taskDefinition' \
  --output text)

if [[ -z "$CURRENT_TD_ARN" || "$CURRENT_TD_ARN" == "None" ]]; then
  echo "[deploy] ERROR: Could not retrieve current task definition ARN for service $SERVICE."
  exit 1
fi

echo "[deploy] Current task definition ARN: $CURRENT_TD_ARN"

# --- Extract taskRoleArn from current task def ---
CURRENT_ROLE_ARN=$(aws ecs describe-task-definition \
  "${AWS_OPTS[@]}" \
  --task-definition "$CURRENT_TD_ARN" \
  --query 'taskDefinition.taskRoleArn' \
  --output text)

# aws CLI returns "None" for null values
if [[ "$CURRENT_ROLE_ARN" == "None" ]]; then
  CURRENT_ROLE_ARN=""
fi

# --- Read new task def JSON ---
NEW_TD_JSON=$(cat "$TASK_DEF_JSON")

# Extract taskRoleArn from new task def (empty string if absent)
NEW_ROLE_ARN=$(echo "$NEW_TD_JSON" | jq -r '.taskRoleArn // ""')

# --- taskRoleArn safeguard ---
if [[ -n "$NEW_ROLE_ARN" ]]; then
  echo "[deploy] taskRoleArn present in new task def: $NEW_ROLE_ARN"
  FINAL_JSON="$NEW_TD_JSON"
elif [[ -n "$CURRENT_ROLE_ARN" ]]; then
  echo "[deploy] Inheriting taskRoleArn from current revision: $CURRENT_ROLE_ARN"
  FINAL_JSON=$(echo "$NEW_TD_JSON" | jq --arg arn "$CURRENT_ROLE_ARN" '. + {taskRoleArn: $arn}')
else
  echo "[deploy] WARNING: No taskRoleArn found in current or new task def. Proceeding without it."
  FINAL_JSON="$NEW_TD_JSON"
fi

# --- Write patched JSON to temp file ---
TMPFILE=$(mktemp /tmp/ecs-td-XXXXXX.json)
trap 'rm -f "$TMPFILE"' EXIT

echo "$FINAL_JSON" > "$TMPFILE"

# --- Register the task definition ---
echo "[deploy] Registering new task definition..."
RESULT=$(aws ecs register-task-definition \
  "${AWS_OPTS[@]}" \
  --cli-input-json "file://$TMPFILE")

NEW_TD_ARN=$(echo "$RESULT" | jq -r '.taskDefinition.taskDefinitionArn')

echo "[deploy] Successfully registered: $NEW_TD_ARN"
echo "$NEW_TD_ARN"
