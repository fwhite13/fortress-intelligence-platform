# AWS Cloud Map — FIP Internal Service Discovery

**ADO#1243** | Setup date: 2026-03-27 | Status: Scripted, pending IAM permissions

## What was set up

A private DNS namespace `fip.internal` in VPC `vpc-0783a9844741980ff` so all FIP ECS services
can call each other via private IP instead of public Cloudflare hostnames (fixes 403 bot
challenge root cause from ADO#1242).

## Internal DNS names

| DNS Name | ECS Service | Port | Notes |
|----------|-------------|------|-------|
| `fait.fip.internal` | `fred-dev` | 8080 | FAIT dev |
| `fait-prod.fip.internal` | `fait-prod` | 8080 | FAIT prod |
| `firm.fip.internal` | `firm-web` | 8080 | FIRM |
| `famos.fip.internal` | `famos-dev` | 8080 | FAMOS |
| `forms.fip.internal` | `formiq-dev` | 8080 | FORMS |
| `fip.fip.internal` | `fip-dev` | 80 | FIP hub |
| `mcp-memory.fip.internal` | `mcp-memory` | 8080 | MCP memory |

## Env vars updated

These are server-to-server calls only. Browser/OAuth redirect URIs are NOT changed.

| Service | Env Var | New Value |
|---------|---------|-----------|
| `firm-web` | `FIP__FaitApiUrl` | `http://fait.fip.internal:8080` |
| `fip-dev` | `Apps__FaitUrl` | `http://fait.fip.internal:8080` |
| `fip-dev` | `Apps__FirmUrl` | `http://firm.fip.internal:8080` |
| `fip-dev` | `Apps__FormsUrl` | `http://forms.fip.internal:8080` |

**NOT changed (browser/OAuth URIs — must stay public):**
- `FIP__FirmCallbackUrl`, `FIP__FaitCallbackUrl`, `FIP__LoginUrl`, `FIP__FormsCallbackUrl`
- `MicrosoftGraph__RedirectUri`, `McpOAuth__RedirectUri`

## IAM permissions required

Add this inline policy to the `fortress-tools-deployer` role before running `cloud-map-setup.sh`:

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Sid": "CloudMapFullAccess",
      "Effect": "Allow",
      "Action": [
        "servicediscovery:*"
      ],
      "Resource": "*"
    },
    {
      "Sid": "Route53HostedZones",
      "Effect": "Allow",
      "Action": [
        "route53:CreateHostedZone",
        "route53:GetHostedZone",
        "route53:ListHostedZones",
        "route53:DeleteHostedZone",
        "route53:ChangeResourceRecordSets",
        "route53:ListResourceRecordSets",
        "route53:GetChange"
      ],
      "Resource": "*"
    },
    {
      "Sid": "EC2VpcDescribe",
      "Effect": "Allow",
      "Action": [
        "ec2:DescribeVpcs",
        "ec2:DescribeSecurityGroups",
        "ec2:AuthorizeSecurityGroupIngress"
      ],
      "Resource": "*"
    }
  ]
}
```

## Security group

SG `sg-0fb53615b1eb4a175` must have a self-referencing inbound rule on TCP 8080
(source = same SG) for container-to-container traffic. Phase 5 of `cloud-map-setup.sh`
checks and prints instructions if the rule is missing.

## How to add a new service

1. Add a row to the ECS_SERVICES and SERVICE_PORTS maps in `cloud-map-setup.sh`
2. Re-run the script — Phase 2 will create the new Cloud Map service, Phase 3 will register it
3. Update the consuming service's env vars (Phase 4 pattern)
4. Run `cloud-map-verify.sh` to confirm

## Rollback steps

### Step 1 — Revert env vars to public hostnames

```bash
REGION="us-east-1"
CLUSTER="fortress-tools-cluster"

# --- firm-web ---
aws ecs describe-task-definition --task-definition firm-web \
  --include TAGS --region $REGION \
  --query 'taskDefinition' --output json > /tmp/td-firm-web.json

python3 - "firm-web" "FIP__FaitApiUrl" "https://fait.dev.fortressam.ai" << 'PYEOF'
import json, sys

task_def = sys.argv[1]
var_name = sys.argv[2]
new_value = sys.argv[3]

with open(f'/tmp/td-{task_def}.json') as f:
    td = json.load(f)

updated = False
for cd in td['containerDefinitions']:
    for env in cd.get('environment', []):
        if env['name'] == var_name:
            old_val = env['value']
            env['value'] = new_value
            print(f"  {task_def}: {var_name}")
            print(f"    old: {old_val}")
            print(f"    new: {new_value}")
            updated = True

# Remove read-only fields before re-registering
for key in ['taskDefinitionArn', 'revision', 'status', 'requiresAttributes',
            'compatibilities', 'registeredAt', 'registeredBy']:
    td.pop(key, None)

with open(f'/tmp/td-{task_def}-new.json', 'w') as f:
    json.dump(td, f, indent=2)

if not updated:
    print(f"  WARNING: {var_name} not found in {task_def} — check env var name")
PYEOF

FIRM_REV=$(aws ecs register-task-definition --region $REGION \
  --cli-input-json file:///tmp/td-firm-web-new.json \
  --query 'taskDefinition.revision' --output text)

aws ecs update-service --cluster $CLUSTER --service firm-web \
  --task-definition firm-web:${FIRM_REV} --region $REGION \
  --query 'service.taskDefinition' --output text

# --- fip-dev (3 sequential updates — each re-describes latest so they chain R1→R2→R3) ---
for args in \
  "fip-dev Apps__FaitUrl  https://fait.dev.fortressam.ai" \
  "fip-dev Apps__FirmUrl  https://firm.dev.fortressam.ai" \
  "fip-dev Apps__FormsUrl https://forms.dev.fortressam.ai"
do
  set -- $args
  task_def=$1 var_name=$2 new_value=$3

  aws ecs describe-task-definition --task-definition $task_def \
    --include TAGS --region $REGION \
    --query 'taskDefinition' --output json > /tmp/td-${task_def}.json

  python3 - "$task_def" "$var_name" "$new_value" << 'PYEOF'
import json, sys

task_def = sys.argv[1]
var_name = sys.argv[2]
new_value = sys.argv[3]

with open(f'/tmp/td-{task_def}.json') as f:
    td = json.load(f)

updated = False
for cd in td['containerDefinitions']:
    for env in cd.get('environment', []):
        if env['name'] == var_name:
            old_val = env['value']
            env['value'] = new_value
            print(f"  {task_def}: {var_name}")
            print(f"    old: {old_val}")
            print(f"    new: {new_value}")
            updated = True

# Remove read-only fields before re-registering
for key in ['taskDefinitionArn', 'revision', 'status', 'requiresAttributes',
            'compatibilities', 'registeredAt', 'registeredBy']:
    td.pop(key, None)

with open(f'/tmp/td-{task_def}-new.json', 'w') as f:
    json.dump(td, f, indent=2)

if not updated:
    print(f"  WARNING: {var_name} not found in {task_def} — check env var name")
PYEOF

  aws ecs register-task-definition --region $REGION \
    --cli-input-json file:///tmp/td-${task_def}-new.json \
    --query 'taskDefinition.revision' --output text
done

FIP_FINAL_REV=$(aws ecs describe-task-definition --task-definition fip-dev \
  --region $REGION --query 'taskDefinition.revision' --output text)

aws ecs update-service --cluster $CLUSTER --service fip-dev \
  --task-definition fip-dev:${FIP_FINAL_REV} --region $REGION \
  --query 'service.taskDefinition' --output text
```

### Step 2 — Remove service registries from ECS services

```bash
CLUSTER="fortress-tools-cluster"
REGION="us-east-1"
for svc in fred-dev fait-prod firm-web famos-dev formiq-dev fip-dev mcp-memory; do
  aws ecs update-service --cluster $CLUSTER --service $svc \
    --service-registries "[]" --region $REGION
done
```

### Step 3 — Delete Cloud Map services and namespace

```bash
REGION="us-east-1"
NS_ID="<namespace_id>"   # get from: aws servicediscovery list-namespaces --region $REGION

for name in fait fait-prod firm famos forms fip mcp-memory; do
  SVC_ID=$(aws servicediscovery list-services --region $REGION \
    --filters Name=NAMESPACE_ID,Values=$NS_ID \
    --query "Services[?Name=='${name}'].Id" --output text)
  aws servicediscovery delete-service --id $SVC_ID --region $REGION
done

aws servicediscovery delete-namespace --id $NS_ID --region $REGION
echo "Route 53 private hosted zone deleted automatically with namespace"
```

### Step 4 — Force new deployments

```bash
for svc in fred-dev fait-prod firm-web famos-dev formiq-dev fip-dev mcp-memory; do
  aws ecs update-service --cluster fortress-tools-cluster --service $svc \
    --force-new-deployment --region us-east-1
done
```

## Scripts

- `cloud-map-setup.sh` — Run once (idempotent) to set everything up. Rhodey runs this.
- `cloud-map-verify.sh` — Run after setup + ECS task startup to confirm. Natasha runs this.
