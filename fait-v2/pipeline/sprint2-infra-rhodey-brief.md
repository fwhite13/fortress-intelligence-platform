# DEPLOY BRIEF — FAIT v2 Sprint 2 Infrastructure Setup
**Rhodey (War Machine) — DevOps | ADO Epic #2835**

## Mission
Set up the AWS infrastructure for FAIT v2 so the first deploy can happen. This is prerequisite infra — not yet deploying Sprint 2 application code (that requires a fresh CodeBuild run first). You are creating the plumbing.

## Use `fortress-tools-deployer` credentials for all AWS operations. NO exceptions.

---

## What Already Exists (DO NOT recreate)

✅ ALB: `fortress-tools-alb` (ARN: `arn:aws:elasticloadbalancing:us-east-1:742932328420:loadbalancer/app/fortress-tools-alb/fe0b167b2404ae04`)
✅ ALB DNS: `fortress-tools-alb-487057611.us-east-1.elb.amazonaws.com`
✅ HTTPS Listener ARN: `arn:aws:elasticloadbalancing:us-east-1:742932328420:listener/app/fortress-tools-alb/fe0b167b2404ae04/03366377561f20e1`
✅ ALB listener rule (priority 10): `fait-v2.dev.fortressam.ai` → `fait-v2-dev-tg` (ALREADY EXISTS, DO NOT recreate)
✅ Target group `fait-v2-dev-tg`: ARN `arn:aws:elasticloadbalancing:us-east-1:742932328420:targetgroup/fait-v2-dev-tg/b81255eae56c643c` (port 8080, ip target type, `/` health check)
✅ ECR repo `fait-v2`: image `742932328420.dkr.ecr.us-east-1.amazonaws.com/fait-v2:bootstrap` (sha256:3bcf852aed06467cf075c6105892e4d5a6ebbbafa0ce22d35062db9e90ddef4c)
✅ IAM task role: `fait-v2-task-role` (ARN: `arn:aws:iam::742932328420:role/fait-v2-task-role`) — created by Fred, already exists
✅ ECS cluster: `fortress-tools-cluster`
✅ Hosted zone: `Z003394436J64H3UMZ756` (fortressam.ai)

---

## Step 1: Route53 CNAME

Create CNAME record `fait-v2.dev.fortressam.ai` → `fortress-tools-alb-487057611.us-east-1.elb.amazonaws.com`

```bash
aws route53 change-resource-record-sets \
  --hosted-zone-id Z003394436J64H3UMZ756 \
  --profile fortress-tools-deployer \
  --change-batch '{
    "Comment": "FAIT v2 dev CNAME",
    "Changes": [{
      "Action": "CREATE",
      "ResourceRecordSet": {
        "Name": "fait-v2.dev.fortressam.ai",
        "Type": "CNAME",
        "TTL": 300,
        "ResourceRecords": [{"Value": "fortress-tools-alb-487057611.us-east-1.elb.amazonaws.com"}]
      }
    }]
  }' 2>&1
```

If this returns `[RRSETAlreadyExists]`: that's fine — skip, it's already done.

---

## Step 2: Fix Target Group Health Check Path

Current health check path is `/` — needs to be `/health` to match the FAIT v2 app's health endpoint.

```bash
aws elbv2 modify-target-group \
  --target-group-arn "arn:aws:elasticloadbalancing:us-east-1:742932328420:targetgroup/fait-v2-dev-tg/b81255eae56c643c" \
  --health-check-path "/health" \
  --health-check-interval-seconds 30 \
  --health-check-timeout-seconds 5 \
  --healthy-threshold-count 2 \
  --unhealthy-threshold-count 3 \
  --profile fortress-tools-deployer --region us-east-1 2>&1
```

---

## Step 3: Register FAIT v2 ECS Task Definition

The `fait-v2` task definition does NOT exist yet. Register it now.

Reference env vars come from `fred-dev` task def (FAIT v1 — same cluster, same pattern).

**Important env vars for FAIT v2 (different from FAIT v1):**
- `FIP_KEYRING_DB_NAME` = `fait_dev` (same shared keyring DB as FAIT v1)
- `FORTRESS_DB_NAME` = `fait_v2_dev` (FAIT v2's own database — separate from FAIT v1's `fait_dev`)
- `Auth__CookieDomain` = `.dev.fortressam.ai` (same as all FIP dev apps)
- `FIP__LoginUrl` = `https://fip.dev.fortressam.ai` (same FIP portal)
- `ASPNETCORE_ENVIRONMENT` = `Development`
- `ASPNETCORE_URLS` = `http://+:8080`
- `AWS_REGION` = `us-east-1`

```bash
aws ecs register-task-definition \
  --profile fortress-tools-deployer --region us-east-1 \
  --family fait-v2 \
  --task-role-arn "arn:aws:iam::742932328420:role/fait-v2-task-role" \
  --execution-role-arn "arn:aws:iam::742932328420:role/fortress-tools-ecs-execution-role" \
  --network-mode awsvpc \
  --requires-compatibilities FARGATE \
  --cpu "512" \
  --memory "1024" \
  --container-definitions '[
    {
      "name": "fait-v2",
      "image": "742932328420.dkr.ecr.us-east-1.amazonaws.com/fait-v2:bootstrap",
      "portMappings": [
        {"containerPort": 8080, "hostPort": 8080, "protocol": "tcp"}
      ],
      "essential": true,
      "environment": [
        {"name": "ASPNETCORE_ENVIRONMENT", "value": "Development"},
        {"name": "ASPNETCORE_URLS", "value": "http://+:8080"},
        {"name": "AWS_REGION", "value": "us-east-1"},
        {"name": "FORTRESS_DB_HOST", "value": "fortress-ai-cluster.cluster-c89acukue4d5.us-east-1.rds.amazonaws.com"},
        {"name": "FORTRESS_DB_PORT", "value": "3306"},
        {"name": "FORTRESS_DB_USER", "value": "fortress_mysql"},
        {"name": "FORTRESS_DB_NAME", "value": "fait_v2_dev"},
        {"name": "FIP_KEYRING_DB_NAME", "value": "fait_dev"},
        {"name": "FIP__LoginUrl", "value": "https://fip.dev.fortressam.ai"},
        {"name": "Auth__CookieDomain", "value": ".dev.fortressam.ai"},
        {"name": "Auth__CookieName", "value": ".FortressAI.Session"},
        {"name": "DataProtection__ApplicationName", "value": "FortressAI"},
        {"name": "FipMcp__EndpointUrl", "value": "https://api.fortressam.ai/mcp"}
      ],
      "secrets": [
        {"name": "FORTRESS_DB_PASS", "valueFrom": "arn:aws:secretsmanager:us-east-1:742932328420:secret:fortress-tools/dev-db-password-9ZKFmr"},
        {"name": "ConnectionStrings__DefaultConnection", "valueFrom": "arn:aws:secretsmanager:us-east-1:742932328420:secret:fait-v2/postgres-master"}
      ],
      "logConfiguration": {
        "logDriver": "awslogs",
        "options": {
          "awslogs-group": "/ecs/fait-v2",
          "awslogs-region": "us-east-1",
          "awslogs-stream-prefix": "ecs"
        }
      }
    }
  ]' 2>&1
```

**If this fails with AccessDenied on `iam:PassRole`:** Report the exact error and halt. Do not try workarounds.

---

## Step 4: Create CloudWatch Log Group

```bash
aws logs create-log-group \
  --log-group-name "/ecs/fait-v2" \
  --profile fortress-tools-deployer --region us-east-1 2>&1
```

If returns `ResourceAlreadyExistsException`: skip, already done.

---

## Step 5: Create ECS Service

```bash
TASK_DEF_ARN=$(aws ecs describe-task-definition --task-definition fait-v2 \
  --profile fortress-tools-deployer --region us-east-1 \
  --query 'taskDefinition.taskDefinitionArn' --output text 2>&1)
echo "Task def ARN: $TASK_DEF_ARN"

aws ecs create-service \
  --cluster fortress-tools-cluster \
  --service-name fait-v2 \
  --task-definition "$TASK_DEF_ARN" \
  --desired-count 1 \
  --launch-type FARGATE \
  --network-configuration 'awsvpcConfiguration={
    subnets=["subnet-08e1d4f1b5530f39e","subnet-051bfcf5b07661809"],
    securityGroups=["sg-0fb53615b1eb4a175"],
    assignPublicIp=ENABLED
  }' \
  --load-balancers '[
    {
      "targetGroupArn": "arn:aws:elasticloadbalancing:us-east-1:742932328420:targetgroup/fait-v2-dev-tg/b81255eae56c643c",
      "containerName": "fait-v2",
      "containerPort": 8080
    }
  ]' \
  --health-check-grace-period-seconds 120 \
  --profile fortress-tools-deployer --region us-east-1 2>&1
```

**If this fails with AccessDenied:** Report exact error and halt.

---

## Step 6: Wait for Service Stability and Health Check

```bash
echo "Waiting for service stability..."
aws ecs wait services-stable \
  --cluster fortress-tools-cluster \
  --services fait-v2 \
  --profile fortress-tools-deployer --region us-east-1 2>&1
echo "Service stable. Checking health..."

# Verify target group health
aws elbv2 describe-target-health \
  --target-group-arn "arn:aws:elasticloadbalancing:us-east-1:742932328420:targetgroup/fait-v2-dev-tg/b81255eae56c643c" \
  --profile fortress-tools-deployer --region us-east-1 \
  --query 'TargetHealthDescriptions[*].{Target:Target.Id,State:TargetHealth.State,Description:TargetHealth.Description}' \
  --output json 2>&1
```

Then test the health endpoint (wait until Route53 propagates, or test via ALB directly):
```bash
curl -s -o /dev/null -w "%{http_code}" https://fait-v2.dev.fortressam.ai/health 2>&1 || echo "DNS not propagated yet — checking via curl with Host header:"
curl -s -o /dev/null -w "%{http_code}" -H "Host: fait-v2.dev.fortressam.ai" http://fortress-tools-alb-487057611.us-east-1.elb.amazonaws.com/health 2>&1
```

**Expected:** HTTP 200 from `/health`

---

## Rollback Plan

If the service fails to stabilize or health checks fail:

```bash
# Scale service to 0
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service fait-v2 \
  --desired-count 0 \
  --profile fortress-tools-deployer --region us-east-1

# Check CloudWatch logs for startup errors
aws logs get-log-events \
  --log-group-name "/ecs/fait-v2" \
  --log-stream-name "$(aws logs describe-log-streams --log-group-name /ecs/fait-v2 --profile fortress-tools-deployer --region us-east-1 --query 'logStreams[0].logStreamName' --output text --order-by LastEventTime --descending 2>&1)" \
  --limit 50 \
  --profile fortress-tools-deployer --region us-east-1 2>&1
```

Report the last 50 log lines if health checks fail.

---

## Deliverables

Write Deploy Report to `/home/fredw/projects/fip/fait-v2/pipeline/SPRINT2-INFRA-DEPLOY-REPORT.md` with:
- Pre-deploy snapshot (N/A for new service — document "new service, no previous state")
- All steps completed (✅/❌ each step)
- Task definition ARN and revision registered
- ECS service ARN
- Target health status
- Health check result (HTTP code from `/health`)
- Rollback plan (commands above)

Then reply with your full Deploy Report.

**Note:** Sprint 2 application code (actual Blazor app with all Sprint 2 features) will be deployed separately once CodeBuild produces the Sprint 2 image. This infra setup step establishes the plumbing so that deploy can happen immediately when the image is ready.
