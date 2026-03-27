#!/usr/bin/env bash
# ADO#1243 — Verify AWS Cloud Map setup for fip.internal
# Run after cloud-map-setup.sh completes and ECS tasks have started.
set -euo pipefail

source ~/projects/ai/projects/fortress_tools/.env.deployer

REGION="us-east-1"
CLUSTER="fortress-tools-cluster"

echo "=== Cloud Map namespace ==="
aws servicediscovery list-namespaces \
  --region "$REGION" \
  --filters "Name=TYPE,Values=DNS_PRIVATE" \
  --query "Namespaces[?Name=='fip.internal'].[Id,Name,Properties.DnsProperties.HostedZoneId]" \
  --output text

echo ""
echo "=== Cloud Map services ==="
NS_ID=$(aws servicediscovery list-namespaces \
  --region "$REGION" \
  --filters "Name=TYPE,Values=DNS_PRIVATE" \
  --query "Namespaces[?Name=='fip.internal'].Id" \
  --output text)

if [[ -z "$NS_ID" || "$NS_ID" == "None" ]]; then
  echo "ERROR: fip.internal namespace not found. Run cloud-map-setup.sh first."
  exit 1
fi

aws servicediscovery list-services \
  --region "$REGION" \
  --filters "Name=NAMESPACE_ID,Values=$NS_ID" \
  --query 'Services[*].[Name,Id,InstanceCount]' \
  --output text | sort

echo ""
echo "=== ECS service registries ==="
for svc in fred-dev fait-prod firm-web famos-dev formiq-dev fip-dev mcp-memory; do
  REG=$(aws ecs describe-services \
    --cluster "$CLUSTER" \
    --services "$svc" \
    --region "$REGION" \
    --query 'services[0].serviceRegistries[*].registryArn' \
    --output text 2>/dev/null || echo "")
  echo "$svc: ${REG:-NONE}"
done

echo ""
echo "=== firm-web: FIP__FaitApiUrl (expect: http://fait.fip.internal:8080) ==="
aws ecs describe-task-definition \
  --task-definition firm-web \
  --region "$REGION" \
  --query "taskDefinition.containerDefinitions[0].environment[?name=='FIP__FaitApiUrl'].value" \
  --output text

echo ""
echo "=== fip-dev: Apps__* (expect internal DNS) ==="
aws ecs describe-task-definition \
  --task-definition fip-dev \
  --region "$REGION" \
  --query "taskDefinition.containerDefinitions[0].environment[?starts_with(name, 'Apps__')].{name:name,value:value}" \
  --output table

echo ""
echo "=== Cloud Map registered instances ==="
for svc_name in fait fait-prod firm famos forms fip mcp-memory; do
  SVC_ID=$(aws servicediscovery list-services \
    --region "$REGION" \
    --filters "Name=NAMESPACE_ID,Values=$NS_ID" \
    --query "Services[?Name=='$svc_name'].Id" \
    --output text)
  if [[ -n "$SVC_ID" && "$SVC_ID" != "None" ]]; then
    COUNT=$(aws servicediscovery list-instances \
      --service-id "$SVC_ID" \
      --region "$REGION" \
      --query 'length(Instances)' \
      --output text 2>/dev/null || echo "0")
    echo "$svc_name ($SVC_ID): $COUNT instance(s)"
  else
    echo "$svc_name: Cloud Map service not found"
  fi
done

echo ""
echo "=== Verify complete ==="
