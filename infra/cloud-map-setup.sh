#!/usr/bin/env bash
# ADO#1243 — AWS Cloud Map private DNS setup for fip.internal
# Idempotent: safe to re-run. All phases check before creating.
# PREREQ: fortress-tools-deployer must have servicediscovery:* + route53:* IAM permissions.
# Rhodey runs this after adding the IAM policy.
set -euo pipefail

source ~/projects/ai/projects/fortress_tools/.env.deployer

VPC_ID="vpc-0783a9844741980ff"
CLUSTER="fortress-tools-cluster"
REGION="us-east-1"
ACCOUNT_ID="742932328420"

# ============================================================
echo "=== Phase 0: IAM check ==="
# ============================================================
if ! aws servicediscovery list-namespaces --region "$REGION" &>/dev/null; then
  echo "ERROR: servicediscovery permissions missing from fortress-tools-deployer."
  echo "Add the policy from infra/CLOUD-MAP.md and retry."
  exit 1
fi
echo "IAM OK"

# ============================================================
echo "=== Phase 1: Create fip.internal namespace ==="
# ============================================================
EXISTING_NS=$(aws servicediscovery list-namespaces \
  --region "$REGION" \
  --filters "Name=TYPE,Values=DNS_PRIVATE" \
  --query "Namespaces[?Name=='fip.internal'].Id" \
  --output text)

if [[ -n "$EXISTING_NS" && "$EXISTING_NS" != "None" ]]; then
  NAMESPACE_ID="$EXISTING_NS"
  echo "Namespace already exists: $NAMESPACE_ID"
else
  OP_ID=$(aws servicediscovery create-private-dns-namespace \
    --name fip.internal \
    --vpc "$VPC_ID" \
    --description "FIP internal service discovery" \
    --region "$REGION" \
    --query 'OperationId' \
    --output text)
  echo "Creating namespace, operation: $OP_ID"

  # Poll until SUCCESS or FAIL (up to 150s)
  for i in $(seq 1 30); do
    STATUS=$(aws servicediscovery get-operation \
      --operation-id "$OP_ID" \
      --region "$REGION" \
      --query 'Operation.Status' \
      --output text)
    echo "[$i/30] Operation status: $STATUS"
    if [[ "$STATUS" == "SUCCESS" ]]; then break; fi
    if [[ "$STATUS" == "FAIL" ]]; then
      echo "ERROR: namespace creation operation failed."
      exit 1
    fi
    sleep 5
  done

  NAMESPACE_ID=$(aws servicediscovery get-operation \
    --operation-id "$OP_ID" \
    --region "$REGION" \
    --query 'Operation.Targets.NAMESPACE' \
    --output text)
fi
echo "NAMESPACE_ID=$NAMESPACE_ID"

# ============================================================
echo "=== Phase 2: Create Cloud Map services ==="
# ============================================================
declare -A SERVICE_IDS

for svc_name in fait fait-prod firm famos forms fip mcp-memory; do
  EXISTING=$(aws servicediscovery list-services \
    --region "$REGION" \
    --filters "Name=NAMESPACE_ID,Values=$NAMESPACE_ID" \
    --query "Services[?Name=='$svc_name'].Id" \
    --output text)

  if [[ -n "$EXISTING" && "$EXISTING" != "None" ]]; then
    SERVICE_IDS[$svc_name]="$EXISTING"
    echo "$svc_name: already exists ($EXISTING)"
  else
    SVC_ID=$(aws servicediscovery create-service \
      --name "$svc_name" \
      --dns-config "NamespaceId=$NAMESPACE_ID,RoutingPolicy=WEIGHTED,DnsRecords=[{Type=A,TTL=10}]" \
      --health-check-custom-config FailureThreshold=1 \
      --region "$REGION" \
      --query 'Service.Id' \
      --output text)
    SERVICE_IDS[$svc_name]="$SVC_ID"
    echo "$svc_name: created ($SVC_ID)"
  fi
done

# ============================================================
echo "=== Phase 3: Register ECS services with Cloud Map ==="
# ============================================================

# Map: Cloud Map service name -> ECS service name
declare -A ECS_SERVICES=(
  [fait]="fred-dev"
  [fait-prod]="fait-prod"
  [firm]="firm-web"
  [famos]="famos-dev"
  [forms]="formiq-dev"
  [fip]="fip-dev"
  [mcp-memory]="mcp-memory"
)
declare -A SERVICE_PORTS=(
  [fait]=8080
  [fait-prod]=8080
  [firm]=8080
  [famos]=8080
  [forms]=8080
  [fip]=80
  [mcp-memory]=8080
)

for svc_name in "${!ECS_SERVICES[@]}"; do
  ecs_svc="${ECS_SERVICES[$svc_name]}"
  svc_id="${SERVICE_IDS[$svc_name]}"
  svc_arn="arn:aws:servicediscovery:${REGION}:${ACCOUNT_ID}:service/${svc_id}"
  port="${SERVICE_PORTS[$svc_name]}"

  # Get the container name from the task definition
  container_name=$(aws ecs describe-task-definition \
    --task-definition "$ecs_svc" \
    --region "$REGION" \
    --query 'taskDefinition.containerDefinitions[0].name' \
    --output text)

  # Check if already registered
  CURRENT=$(aws ecs describe-services \
    --cluster "$CLUSTER" \
    --services "$ecs_svc" \
    --region "$REGION" \
    --query 'services[0].serviceRegistries[*].registryArn' \
    --output text 2>/dev/null || echo "")

  if echo "$CURRENT" | grep -q "$svc_id"; then
    echo "$ecs_svc: already registered with Cloud Map"
  else
    echo "Registering $ecs_svc -> $svc_name.fip.internal:$port (container: $container_name)"
    aws ecs update-service \
      --cluster "$CLUSTER" \
      --service "$ecs_svc" \
      --service-registries "registryArn=${svc_arn},containerName=${container_name},containerPort=${port}" \
      --region "$REGION" \
      --query 'service.serviceName' \
      --output text
  fi
done

# ============================================================
echo "=== Phase 4: Update env vars to internal DNS ==="
# ============================================================

# update_env_var: describe latest task def, update one env var, register new revision
# Always re-describes to chain correctly across multiple calls on same task def.
update_env_var() {
  local task_def="$1"
  local var_name="$2"
  local new_value="$3"

  echo "  Updating $task_def: $var_name -> $new_value"

  # Always describe latest active revision (not cached)
  aws ecs describe-task-definition \
    --task-definition "$task_def" \
    --include TAGS \
    --region "$REGION" \
    --query 'taskDefinition' \
    --output json > "/tmp/td-${task_def}.json"

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

  REV=$(aws ecs register-task-definition \
    --region "$REGION" \
    --cli-input-json "file:///tmp/td-${task_def}-new.json" \
    --query 'taskDefinition.revision' \
    --output text)
  echo "  Registered ${task_def}:${REV}"
  echo "$REV"
}

# --- firm-web: update FIP__FaitApiUrl ---
update_env_var "firm-web" "FIP__FaitApiUrl" "http://fait.fip.internal:8080"
FIRM_REV=$(aws ecs describe-task-definition \
  --task-definition firm-web \
  --region "$REGION" \
  --query 'taskDefinition.revision' \
  --output text)
echo "Deploying firm-web:$FIRM_REV"
aws ecs update-service \
  --cluster "$CLUSTER" \
  --service firm-web \
  --task-definition "firm-web:${FIRM_REV}" \
  --region "$REGION" \
  --query 'service.taskDefinition' \
  --output text

# --- fip-dev: update 3 env vars sequentially ---
# Each call re-describes latest revision, so they chain: R1 -> R2 -> R3
update_env_var "fip-dev" "Apps__FaitUrl"  "http://fait.fip.internal:8080"
update_env_var "fip-dev" "Apps__FirmUrl"  "http://firm.fip.internal:8080"
update_env_var "fip-dev" "Apps__FormsUrl" "http://forms.fip.internal:8080"

FIP_FINAL_REV=$(aws ecs describe-task-definition \
  --task-definition fip-dev \
  --region "$REGION" \
  --query 'taskDefinition.revision' \
  --output text)
echo "Deploying fip-dev:$FIP_FINAL_REV"
aws ecs update-service \
  --cluster "$CLUSTER" \
  --service fip-dev \
  --task-definition "fip-dev:${FIP_FINAL_REV}" \
  --region "$REGION" \
  --query 'service.taskDefinition' \
  --output text

# ============================================================
echo "=== Phase 5: Security group self-referencing rule check ==="
# ============================================================
SG_ID="sg-0fb53615b1eb4a175"
echo "Checking inbound rules on SG $SG_ID for port 8080..."
aws ec2 describe-security-groups \
  --group-ids "$SG_ID" \
  --region "$REGION" \
  --query 'SecurityGroups[0].IpPermissions[?FromPort==`8080`]' \
  --output json

echo ""
echo "ACTION REQUIRED (manual): Verify the above output contains a self-referencing rule"
echo "  (UserIdGroupPairs with GroupId == $SG_ID)."
echo "  If no such rule exists, Rhodey must add it:"
echo "    aws ec2 authorize-security-group-ingress \\"
echo "      --group-id $SG_ID \\"
echo "      --protocol tcp --port 8080 \\"
echo "      --source-group $SG_ID \\"
echo "      --region $REGION"

# ============================================================
echo "=== Phase 6: Force new deployments ==="
# ============================================================
for svc in fred-dev fait-prod firm-web famos-dev formiq-dev fip-dev mcp-memory; do
  echo "Force-deploying $svc..."
  aws ecs update-service \
    --cluster "$CLUSTER" \
    --service "$svc" \
    --force-new-deployment \
    --region "$REGION" \
    --query 'service.deployments[0].status' \
    --output text
done

echo ""
echo "=== Cloud Map setup complete ==="
echo "Services should register within ~30s of ECS task startup."
echo "Verify with: infra/cloud-map-verify.sh"
