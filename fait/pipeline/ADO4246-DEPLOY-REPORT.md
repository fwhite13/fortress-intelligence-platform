# ADO#4246 Deploy Report — CloudFront Signed URLs for Office Online PPTX/XLSX Preview

**Date:** 2026-05-27  
**Commit:** `65699baa`  
**Status:** 🔴 BLOCKED — Stage A requires Console intervention (IAM gaps documented below)

---

## Pre-Deploy Snapshot (Rollback Target)

| Field | Value |
|-------|-------|
| Cluster | `fortress-tools-cluster` |
| Service | `fred-dev` |
| **Rollback target task def** | `fred-dev:288` |
| Task role | `arn:aws:iam::742932328420:role/fortress-tools-ecs-task-role` |

**Rollback command (if needed):**
```bash
source /home/fredw/projects/ai/projects/fortress_tools/.env.deployer
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service fred-dev \
  --task-definition red-dev:288 \
  --force-new-deployment \
  --region us-east-1 \
  --profile fortress-tools-deployer
```

---

## Stage A: AWS Infrastructure — STATUS: BLOCKED (Console Required)

### IAM Gaps — `fortress-tools-deployer` Lacks These Permissions

The following operations were attempted and failed with `AccessDenied`:

| Operation | Required Action | Blocked? |
|-----------|----------------|----------|
| Register CloudFront public key | `cloudfront:CreatePublicKey` | ✅ BLOCKED |
| Create CloudFront key group | `cloudfront:CreateKeyGroup` | ✅ BLOCKED |
| Create CloudFront OAC | `cloudfront:CreateOriginAccessControl` | ✅ BLOCKED |
| Create CloudFront distribution | `cloudfront:CreateDistribution` | ✅ BLOCKED |
| List CloudFront distributions | `cloudfront:ListDistributions` | ✅ BLOCKED |
| Get S3 bucket policy | `s3:GetBucketPolicy` | ✅ BLOCKED |
| Put S3 bucket policy | `s3:PutBucketPolicy` | ✅ BLOCKED |
| Create Secrets Manager secret | `secretsmanager:CreateSecret` | ✅ BLOCKED |
| List Secrets Manager secrets | `secretsmanager:ListSecrets` | ✅ BLOCKED |

### ✅ COMPLETED — IAM Task Role Policy Added

The ECS task role (`fortress-tools-ecs-task-role`) has been updated to allow the app to read the CloudFront private key from Secrets Manager at runtime:

```json
{
  "PolicyName": "fait-cloudfront-secrets-policy",
  "Statement": [{
    "Effect": "Allow",
    "Action": ["secretsmanager:GetSecretValue"],
    "Resource": "arn:aws:secretsmanager:us-east-1:742932328420:secret:fait/cloudfront/*"
  }]
}
```

### 🔑 Keys Generated

RSA 2048 key pair was generated and stored securely:
- **Private key:** `/home/fredw/.openclaw/agents/devops/pipeline/ado4246-keys/cloudfront-signing-key.pem` (600 perms)
- **Public key:** `/home/fredw/.openclaw/agents/devops/pipeline/ado4246-keys/cloudfront-signing-key-pub.pem` (600 perms)

**Public key content (paste into CloudFront Console):**
```
-----BEGIN PUBLIC KEY-----
MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAnwGwj4F3WfG1WMvkehhs
qQSK+SeCosEdEarwth9kxk1ODd/gOgJ9tT0Xd4hDEB7H9JaBbIgh+hhdD26l48yT
hS21dcQevMDkcEORZOw3rveXJwfRgwAC3SMaV1hWlYDWPDdNAKyL7rqM+5urHTr7
952gitNiRn+rdW64IHfLRASbhSqrf4x9iKhPvirAFRh6PXXpgvy0H+zdIqigyPOJ
oGInAX+70PpusnUKwYeCSBRmxj1do+cdUpZ4z6qg4haSDFWgoSYMAMFaT8eZ0SAp
PKu6BmbfpbBUxDgxF0W6wPYkTYVEfStUgPydd+NinFBZo8g61jadZgDFaoDD3Gcr
WQIDAQAB
-----END PUBLIC KEY-----
```

---

## 📋 CONSOLE STEPS FOR FRED (Stage A — All 6 Steps)

Complete these in order. Note the IDs at each step — needed for the next.

### Step 1 — CloudFront Public Key
**URL:** https://us-east-1.console.aws.amazon.com/cloudfront/v3/home#/publickey

1. Click **Create public key**
2. Name: `fait-workspace-signing-key`
3. Comment: `FAIT workspace file preview signing key`
4. Key value: paste the public key above (full PEM block including headers)
5. Click **Create**
6. **Record the Key ID** → `CLOUDFRONT_KEY_PAIR_ID`

### Step 2 — CloudFront Key Group
**URL:** https://us-east-1.console.aws.amazon.com/cloudfront/v3/home#/keygroups

1. Click **Create key group**
2. Name: `fait-workspace-key-group`
3. Comment: `FAIT workspace key group`
4. Public keys: select `fait-workspace-signing-key` (the key from Step 1)
5. Click **Create key group**
6. **Record the Key Group ID** → `CLOUDFRONT_KEY_GROUP_ID`

### Step 3 — CloudFront Origin Access Control (OAC)
**URL:** https://us-east-1.console.aws.amazon.com/cloudfront/v3/home#/oac/create

1. Click **Create control setting**
2. Name: `fait-workspace-oac`
3. Description: `OAC for fortress-user-workspaces bucket`
4. Origin type: **S3**
5. Signing behavior: **Sign requests (recommended)**
6. Click **Create**
7. **Record the OAC ID** → `CLOUDFRONT_OAC_ID`

### Step 4 — CloudFront Distribution
**URL:** https://us-east-1.console.aws.amazon.com/cloudfront/v3/home#/distributions/create

1. **Origin domain:** `fortress-user-workspaces.s3.us-east-1.amazonaws.com`
2. **Origin access:** Select **Origin access control settings (recommended)** → select `fait-workspace-oac`
3. **Viewer protocol policy:** HTTPS only
4. **Cache policy:** CachingDisabled (or Managed-CachingOptimized)
5. Under **Restrict viewer access:** Enable → **Trusted key groups** → add `fait-workspace-key-group`
6. **Web Application Firewall (WAF):** Do not enable (unless required)
7. **HTTP version:** HTTP/2
8. **Comment:** `FAIT workspace files for Office Online preview`
9. Click **Create distribution**
10. **Record:** Distribution ID, and **Domain name** (e.g. `d1234abcd.cloudfront.net`) → `CLOUDFRONT_DOMAIN`

> ⏰ Distribution takes ~5-10 minutes to deploy (status changes from "In Progress" to "Enabled")

### Step 5 — S3 Bucket Policy for `fortress-user-workspaces`
**URL:** https://s3.console.aws.amazon.com/s3/buckets/fortress-user-workspaces?tab=permissions

1. Click **Bucket Policy** → Edit
2. ⚠️ **IMPORTANT:** Merge with any existing statements — do NOT replace
3. Add this statement (replace `CLOUDFRONT_DISTRIBUTION_ID` and `ACCOUNT_ID=742932328420`):

```json
{
  "Sid": "AllowCloudFrontOAC",
  "Effect": "Allow",
  "Principal": {"Service": "cloudfront.amazonaws.com"},
  "Action": "s3:GetObject",
  "Resource": "arn:aws:s3:::fortress-user-workspaces/*",
  "Condition": {
    "StringEquals": {
      "AWS:SourceArn": "arn:aws:cloudfront::742932328420:distribution/CLOUDFRONT_DISTRIBUTION_ID"
    }
  }
}
```

### Step 6 — Secrets Manager — Store Private Key
**URL:** https://us-east-1.console.aws.amazon.com/secretsmanager/newsecret

1. **Secret type:** Other type of secret
2. **Plaintext** tab → paste the entire contents of: `/home/fredw/.openclaw/agents/devops/pipeline/ado4246-keys/cloudfront-signing-key.pem`
3. **Secret name:** `fait/cloudfront/workspace-signing-key`
4. **Description:** `CloudFront RSA private key for FAIT workspace file preview signed URLs`
5. No rotation
6. Click **Store**
7. **Record the full secret ARN**

> 🔒 After storing: delete the local key file:
> ```bash
> rm /home/fredw/.openclaw/agents/devops/pipeline/ado4246-keys/cloudfront-signing-key.pem
> rm /home/fredw/.openclaw/agents/devops/pipeline/ado4246-keys/cloudfront-signing-key-pub.pem
> ```

---

## Stage B: App Deploy — STATUS: READY (pending Stage A values)

Stage B will proceed once Fred provides:
1. `CLOUDFRONT_DOMAIN` (e.g. `d1234abcd.cloudfront.net`)
2. `CLOUDFRONT_KEY_PAIR_ID` (the Public Key ID from Step 1, NOT the Key Group ID)

### B1 — Current State
- Current task def: `fred-dev:288`
- Commit: `65699baa` (confirmed at monorepo root)
- Build ready: monorepo at `/home/fredw/projects/fip`

### B2-B3 — Docker Build + ECR Push Commands (ready to run)

```bash
source /home/fredw/projects/ai/projects/fortress_tools/.env.deployer
cd /home/fredw/projects/fip
git pull --ff-only origin main  # confirm still at 65699baa
COMMIT_SHA=$(git rev-parse --short HEAD)
ACCOUNT_ID=742932328420
ECR_REPO="$ACCOUNT_ID.dkr.ecr.us-east-1.amazonaws.com/fred-chat"

# Build
docker build --no-cache \
  -f fait/Dockerfile.debian \
  -t fred-chat:$COMMIT_SHA \
  . 2>&1 | tee /tmp/ado4246-build.log
echo "Build exit: $?"

# ECR push
aws ecr get-login-password --region us-east-1 --profile fortress-tools-deployer | \
  docker login --username AWS --password-stdin $ECR_REPO
docker tag fred-chat:$COMMIT_SHA $ECR_REPO:$COMMIT_SHA
docker push $ECR_REPO:$COMMIT_SHA
```

### B4 — Task Def + ECS Deploy (run after Stage A)

After Stage A provides values, run:
```bash
source /home/fredw/projects/ai/projects/fortress_tools/.env.deployer

# Fill these in from Stage A
CLOUDFRONT_DOMAIN="REPLACE_ME"
CLOUDFRONT_KEY_PAIR_ID="REPLACE_ME"
COMMIT_SHA=$(cd /home/fredw/projects/fip && git rev-parse --short HEAD)
ACCOUNT_ID=742932328420
ECR_REPO="$ACCOUNT_ID.dkr.ecr.us-east-1.amazonaws.com/fred-chat"

# Get current task def
aws ecs describe-task-definition \
  --task-definition fred-dev:288 \
  --region us-east-1 \
  --profile fortress-tools-deployer > /tmp/current-fred-dev-taskdef.json

# Update container image and add CloudFront env vars (use jq or Python to merge)
python3 << 'PYEOF'
import json, subprocess, sys

with open('/tmp/current-fred-dev-taskdef.json') as f:
    data = json.load(f)

td = data['taskDefinition']
container = td['containerDefinitions'][0]

# Update image
import os
commit_sha = subprocess.check_output(['git','-C','/home/fredw/projects/fip','rev-parse','--short','HEAD']).decode().strip()
container['image'] = f"742932328420.dkr.ecr.us-east-1.amazonaws.com/fred-chat:{commit_sha}"

# Add/update CloudFront env vars
cf_vars = {
    'CloudFront__DistributionDomain': os.environ.get('CLOUDFRONT_DOMAIN', 'REPLACE_ME'),
    'CloudFront__KeyPairId': os.environ.get('CLOUDFRONT_KEY_PAIR_ID', 'REPLACE_ME'),
    'CloudFront__PrivateKeySecretName': 'fait/cloudfront/workspace-signing-key',
    'CloudFront__UrlExpirySeconds': '3600',
}

env = container.get('environment', [])
existing_names = {e['name'] for e in env}
for k, v in cf_vars.items():
    if k in existing_names:
        for e in env:
            if e['name'] == k:
                e['value'] = v
    else:
        env.append({'name': k, 'value': v})
container['environment'] = env

# Build new task def registration payload
new_td = {
    'family': td['family'],
    'taskRoleArn': td.get('taskRoleArn',''),
    'executionRoleArn': td.get('executionRoleArn',''),
    'networkMode': td['networkMode'],
    'containerDefinitions': td['containerDefinitions'],
    'volumes': td.get('volumes', []),
    'placementConstraints': td.get('placementConstraints', []),
    'requiresCompatibilities': td.get('requiresCompatibilities', []),
    'cpu': td['cpu'],
    'memory': td['memory'],
}

with open('/tmp/new-fred-dev-taskdef.json', 'w') as f:
    json.dump(new_td, f, indent=2)
print(f"New task def written with image fred-chat:{commit_sha}")
print("CloudFront env vars added")
PYEOF

# Register new task def
NEW_TD=$(aws ecs register-task-definition \
  --region us-east-1 \
  --profile fortress-tools-deployer \
  --cli-input-json file:///tmp/new-fred-dev-taskdef.json \
  --query 'taskDefinition.taskDefinitionArn' \
  --output text)
echo "New task def: $NEW_TD"

# Force new deployment
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service fred-dev \
  --task-definition $NEW_TD \
  --force-new-deployment \
  --region us-east-1 \
  --profile fortress-tools-deployer

# Wait for stable
aws ecs wait services-stable \
  --cluster fortress-tools-cluster \
  --services fred-dev \
  --region us-east-1 \
  --profile fortress-tools-deployer
echo "ECS stable"
```

---

## IAM Gaps Summary — Flag to Maria

The following permissions need to be added to `fortress-tools-deployer` for future automated deployments that include CloudFront/SM infrastructure:

| Permission | Service | Required For |
|-----------|---------|-------------|
| `cloudfront:CreatePublicKey` | CloudFront | Registering signing keys |
| `cloudfront:CreateKeyGroup` | CloudFront | Creating trusted key groups |
| `cloudfront:CreateOriginAccessControl` | CloudFront | Creating OAC for S3 origins |
| `cloudfront:CreateDistribution` | CloudFront | Creating distributions |
| `cloudfront:ListDistributions` | CloudFront | Read access |
| `s3:GetBucketPolicy` | S3 | Reading existing bucket policies |
| `s3:PutBucketPolicy` | S3 | Updating bucket policies |
| `secretsmanager:CreateSecret` | Secrets Manager | Creating new secrets |
| `secretsmanager:PutSecretValue` | Secrets Manager | Updating secrets |
| `secretsmanager:ListSecrets` | Secrets Manager | Listing secrets |

---

## What Was Completed Automatically

| Step | Status | Notes |
|------|--------|-------|
| Pre-deploy snapshot | ✅ | `fred-dev:288` recorded |
| RSA 2048 key pair generated | ✅ | Stored at `/home/fredw/.openclaw/agents/devops/pipeline/ado4246-keys/` |
| Temp key files deleted | ✅ | `/tmp/cloudfront-signing-key*.pem` removed |
| IAM task role policy added | ✅ | `fait-cloudfront-secrets-policy` on `fortress-tools-ecs-task-role` |
| A1 CloudFront public key register | ❌ BLOCKED | `cloudfront:CreatePublicKey` denied |
| A2 CloudFront key group | ❌ BLOCKED | `cloudfront:CreateKeyGroup` denied |
| A3 Origin Access Control | ❌ BLOCKED | `cloudfront:CreateOriginAccessControl` denied |
| A4 CloudFront distribution | ❌ BLOCKED | `cloudfront:CreateDistribution` denied |
| A5 S3 bucket policy | ❌ BLOCKED | `s3:PutBucketPolicy` denied |
| A6 Secrets Manager create | ❌ BLOCKED | `secretsmanager:CreateSecret` denied |
| Stage B build/deploy | ⏸ WAITING | Needs Stage A values |

---

## Rollback Plan

**If Stage B fails:** Force-new-deployment with `fred-dev:288`
```bash
source /home/fredw/projects/ai/projects/fortress_tools/.env.deployer
aws ecs update-service --cluster fortress-tools-cluster --service fred-dev \
  --task-definition fred-dev:288 --force-new-deployment \
  --region us-east-1 --profile fortress-tools-deployer
```

**CloudFront infra (if distribution is broken):** Disable via Console. Do NOT attempt immediate deletion (requires disabled + propagation first). App falls back to S3 presigned URLs automatically when `CloudFront__DistributionDomain` is not configured.

---

*Deploy agent: rhodey-ado4246 | Session: agent:devops:subagent:eb71079a*
