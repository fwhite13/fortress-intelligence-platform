#!/bin/bash
set -e

PROFILE="fortress-tools-deployer"
REGION="us-east-1"
ACCOUNT="742932328420"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

echo "=== [1/7] Creating Lambda IAM role ==="
aws iam create-role \
  --role-name vpbot-scale-guard-role \
  --assume-role-policy-document file://${SCRIPT_DIR}/lambda-trust-policy.json \
  --profile $PROFILE

aws iam put-role-policy \
  --role-name vpbot-scale-guard-role \
  --policy-name vpbot-scale-guard-policy \
  --policy-document file://${SCRIPT_DIR}/lambda-role-policy.json \
  --profile $PROFILE

LAMBDA_ROLE_ARN="arn:aws:iam::${ACCOUNT}:role/vpbot-scale-guard-role"

echo "Waiting 15s for IAM role propagation..."
sleep 15

echo "=== [2/7] Creating vpbot-scale-guard Lambda ==="
cd "${SCRIPT_DIR}"
zip vpbot-scale-guard.zip index.py
aws lambda create-function \
  --function-name vpbot-scale-guard \
  --runtime python3.12 \
  --role "${LAMBDA_ROLE_ARN}" \
  --handler index.handler \
  --zip-file fileb://vpbot-scale-guard.zip \
  --environment "Variables={ECS_CLUSTER=fortress-tools-cluster,ECS_SERVICE=meetings-vpbot-dev}" \
  --timeout 30 \
  --profile $PROFILE --region $REGION

SCALE_GUARD_ARN="arn:aws:lambda:${REGION}:${ACCOUNT}:function:vpbot-scale-guard"
echo "vpbot-scale-guard ARN: ${SCALE_GUARD_ARN}"

echo "=== [3/7] Creating vpbot-scale-up Lambda ==="
zip vpbot-scale-up.zip scale_up.py
aws lambda create-function \
  --function-name vpbot-scale-up \
  --runtime python3.12 \
  --role "${LAMBDA_ROLE_ARN}" \
  --handler scale_up.handler \
  --zip-file fileb://vpbot-scale-up.zip \
  --environment "Variables={ECS_CLUSTER=fortress-tools-cluster,ECS_SERVICE=meetings-vpbot-dev}" \
  --timeout 30 \
  --profile $PROFILE --region $REGION

SCALE_UP_ARN="arn:aws:lambda:${REGION}:${ACCOUNT}:function:vpbot-scale-up"
echo "vpbot-scale-up ARN: ${SCALE_UP_ARN}"

echo "=== [4/7] Creating Scheduler IAM role ==="
aws iam create-role \
  --role-name vpbot-scheduler-role \
  --assume-role-policy-document file://${SCRIPT_DIR}/scheduler-trust-policy.json \
  --profile $PROFILE

aws iam put-role-policy \
  --role-name vpbot-scheduler-role \
  --policy-name vpbot-scheduler-policy \
  --policy-document file://${SCRIPT_DIR}/scheduler-role-policy.json \
  --profile $PROFILE

SCHEDULER_ROLE_ARN="arn:aws:iam::${ACCOUNT}:role/vpbot-scheduler-role"

echo "Waiting 15s for IAM role propagation..."
sleep 15

echo "=== [5/7] Creating scale-UP schedule ==="
aws scheduler create-schedule \
  --name vpbot-scale-up \
  --schedule-expression "cron(0 7 * * ? *)" \
  --schedule-expression-timezone "America/New_York" \
  --flexible-time-window '{"Mode":"OFF"}' \
  --target "{\"Arn\": \"${SCALE_UP_ARN}\", \"RoleArn\": \"${SCHEDULER_ROLE_ARN}\", \"Input\": \"{}\"}" \
  --profile $PROFILE --region $REGION

echo "=== [6/7] Creating scale-DOWN schedule ==="
aws scheduler create-schedule \
  --name vpbot-scale-down \
  --schedule-expression "cron(0 21 * * ? *)" \
  --schedule-expression-timezone "America/New_York" \
  --flexible-time-window '{"Mode":"OFF"}' \
  --target "{\"Arn\": \"${SCALE_GUARD_ARN}\", \"RoleArn\": \"${SCHEDULER_ROLE_ARN}\", \"Input\": \"{}\"}" \
  --profile $PROFILE --region $REGION

echo "=== [7/7] Cleaning up zip files ==="
rm -f vpbot-scale-guard.zip vpbot-scale-up.zip

echo ""
echo "=== DEPLOY COMPLETE ==="
echo "Scale-up:   cron(0 7 * * ? *) America/New_York → vpbot-scale-up Lambda → ECS desiredCount=1"
echo "Scale-down: cron(0 21 * * ? *) America/New_York → vpbot-scale-guard Lambda → ECS desiredCount=0 (with active-meeting guard)"
echo ""
echo "Verify with:"
echo "  aws scheduler list-schedules --profile ${PROFILE} --region ${REGION}"
echo "  aws lambda list-functions --profile ${PROFILE} --region ${REGION} | grep vpbot"
