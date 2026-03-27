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

For `firm-web`:
```bash
# Describe current task def, change FIP__FaitApiUrl back to https://fait.dev.fortressam.ai
# Register new revision, update-service
```

For `fip-dev`:
```bash
# Change Apps__FaitUrl, Apps__FirmUrl, Apps__FormsUrl back to public hostnames
# Register new revision, update-service
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
# Delete each Cloud Map service (must deregister instances first)
# Then delete the fip.internal namespace
# Route 53 private hosted zone is deleted automatically with namespace
```

### Step 4 — Force new deployments

```bash
for svc in red-dev fait-prod firm-web famos-dev formiq-dev fip-dev mcp-memory; do
  aws ecs update-service --cluster fortress-tools-cluster --service $svc \
    --force-new-deployment --region us-east-1
done
```

## Scripts

- `cloud-map-setup.sh` — Run once (idempotent) to set everything up. Rhodey runs this.
- `cloud-map-verify.sh` — Run after setup + ECS task startup to confirm. Natasha runs this.
