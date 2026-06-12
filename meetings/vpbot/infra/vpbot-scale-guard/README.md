# vpbot Scheduled Scaling — Infrastructure

## Overview

Implements daily scheduled warm/cold scaling for the `meetings-vpbot-dev` ECS service:

- **Scale-up**: 7:00 AM ET daily — ensures vpbot is ready for the business day
- **Scale-down**: 9:00 PM ET daily — shuts down the service overnight to reduce cost
- **Active-meeting guard**: scale-down is skipped if any ECS task has been running < 30 minutes (likely an active meeting in progress)

## Architecture

```
EventBridge Scheduler: vpbot-scale-up
  cron(0 12 * * ? *) UTC = 7:00 AM ET
    → Lambda: vpbot-scale-up
        → ECS UpdateService desiredCount=1

EventBridge Scheduler: vpbot-scale-down
  cron(0 2 * * ? *) UTC = 9:00 PM ET
    → Lambda: vpbot-scale-guard
        → Check task age:
            if any task < 30 min old → skip scale-down (log and return)
            if all tasks ≥ 30 min old, or no tasks → UpdateService desiredCount=0
```

**Active-meeting guard logic:** At scale-down time, the Lambda describes all running tasks. If any task started less than 30 minutes ago, it is assumed an active meeting is in progress and scale-down is skipped. Tasks older than 30 minutes are considered stale and will be scaled down.

## Files

| File | Purpose |
|------|---------|
| `index.py` | Scale-down guard Lambda handler |
| `scale-up.py` | Scale-up Lambda handler |
| `lambda-role-policy.json` | Lambda execution role inline policy (ECS + CloudWatch Logs) |
| `lambda-trust-policy.json` | Lambda execution role trust policy |
| `scheduler-role-policy.json` | EventBridge Scheduler role inline policy (`lambda:InvokeFunction` on both Lambdas) |
| `scheduler-trust-policy.json` | EventBridge Scheduler role trust policy |
| `deploy.sh` | One-shot deploy script — run from `infra/vpbot-scale-guard/` |

## Required IAM Grants for `fortress-tools-deployer`

Before running `deploy.sh`, the `fortress-tools-deployer` IAM user/role must have these permissions:

```
iam:CreateRole
iam:PutRolePolicy
iam:GetRole
lambda:CreateFunction
lambda:GetFunction
scheduler:CreateSchedule
scheduler:ListSchedules
logs:CreateLogGroup  (optional — Lambda creates this automatically)
```

Suggested inline policy name: `vpbot-deploy-policy`

These should be scoped to specific resources:

- **IAM:** `arn:aws:iam::742932328420:role/vpbot-*`
- **Lambda:** `arn:aws:lambda:us-east-1:742932328420:function:vpbot-*`
- **Scheduler:** `arn:aws:scheduler:us-east-1:742932328420:schedule/default/vpbot-*`

Rhodey needs to grant these before `deploy.sh` can run.

## Deployment

```bash
cd infra/vpbot-scale-guard
chmod +x deploy.sh
./deploy.sh
```

The script creates in order:
1. Lambda IAM role (`vpbot-scale-guard-role`) with inline policy
2. `vpbot-scale-guard` Lambda (scale-down guard)
3. `vpbot-scale-up` Lambda
4. Scheduler IAM role (`vpbot-scheduler-role`) with inline policy
5. EventBridge Scheduler rule: `vpbot-scale-up` (7 AM ET daily)
6. EventBridge Scheduler rule: `vpbot-scale-down` (9 PM ET daily)

## Verification

After deploy:

```bash
aws scheduler list-schedules --profile fortress-tools-deployer --region us-east-1
aws lambda list-functions --profile fortress-tools-deployer --region us-east-1 | grep vpbot
```

## Rollback

```bash
aws scheduler delete-schedule --name vpbot-scale-up --profile fortress-tools-deployer --region us-east-1
aws scheduler delete-schedule --name vpbot-scale-down --profile fortress-tools-deployer --region us-east-1
aws lambda delete-function --function-name vpbot-scale-guard --profile fortress-tools-deployer --region us-east-1
aws lambda delete-function --function-name vpbot-scale-up --profile fortress-tools-deployer --region us-east-1
aws iam delete-role-policy --role-name vpbot-scale-guard-role --policy-name vpbot-scale-guard-policy --profile fortress-tools-deployer
aws iam delete-role --role-name vpbot-scale-guard-role --profile fortress-tools-deployer
aws iam delete-role-policy --role-name vpbot-scheduler-role --policy-name vpbot-scheduler-policy --profile fortress-tools-deployer
aws iam delete-role --role-name vpbot-scheduler-role --profile fortress-tools-deployer
```
