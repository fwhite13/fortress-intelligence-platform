# Build Report — ADO#1243: AWS Cloud Map private DNS

**Engineer:** Tony Stark
**Build cycle:** 1
**Date:** 2026-03-27
**Status:** SUCCEEDED (scripts only — no app code changes)

## Files created

| File | Purpose |
|------|---------|
| `infra/cloud-map-setup.sh` | 6-phase idempotent setup script — Rhodey runs this |
| `infra/cloud-map-verify.sh` | Verification script — Natasha runs this |
| `infra/CLOUD-MAP.md` | Operations runbook |
| `infra/ADO1243-BUILD-REPORT.md` | This file |

## Phase-by-phase description

| Phase | Description |
|-------|-------------|
| 0 | IAM check — fails fast if `servicediscovery:*` not available |
| 1 | Create `fip.internal` private DNS namespace in `vpc-0783a9844741980ff` (idempotent) |
| 2 | Create 7 Cloud Map services (fait, fait-prod, firm, famos, forms, fip, mcp-memory) — idempotent |
| 3 | Register 7 ECS services with Cloud Map service registries (idempotent check) |
| 4 | Update task definition env vars: `FIP__FaitApiUrl` on firm-web, `Apps__FaitUrl`/`Apps__FirmUrl`/`Apps__FormsUrl` on fip-dev |
| 5 | Security group check — prints instructions if self-referencing rule on port 8080 is missing |
| 6 | Force new deployments on all 7 ECS services |

## Env vars updated

| Service | Var | Old | New |
|---------|-----|-----|-----|
| firm-web | `FIP__FaitApiUrl` | `https://fait.dev.fortressam.ai` | `http://fait.fip.internal:8080` |
| fip-dev | `Apps__FaitUrl` | `https://fait.dev.fortressam.ai` | `http://fait.fip.internal:8080` |
| fip-dev | `Apps__FirmUrl` | `https://firm.dev.fortressam.ai` | `http://firm.fip.internal:8080` |
| fip-dev | `Apps__FormsUrl` | `https://forms.dev.fortressam.ai` | `http://forms.fip.internal:8080` |

## IAM permissions required (Rhodey)

The `fortress-tools-deployer` IAM role needs this inline policy before running the script:

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Sid": "CloudMapFullAccess",
      "Effect": "Allow",
      "Action": ["servicediscovery:*"],
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

## ⚠️ BLOCKED — Action required before execution

**Script is ready. Cannot run until Rhodey adds the IAM policy.**

Steps for Rhodey:
1. Go to IAM → Roles → `fortress-tools-deployer`
2. Add the inline policy above (name it `CloudMapSetup`)
3. Run `infra/cloud-map-setup.sh`
4. Monitor Phase 0 — if IAM check passes, all phases will proceed
5. After ~60s, ask Natasha to run `infra/cloud-map-verify.sh`

## CC sessions

1 CC session, sequential (no parallelization needed — pure file writing, no build dependencies).

## No app code changes

This is pure infrastructure. No `.cs`, `.razor`, `.csproj`, or application files were modified.
Task definitions are updated at runtime by the script (not committed to the repo).

## Known edge cases / things to scrutinize

- **fip-dev 3-update chaining:** Each `update_env_var` call re-describes the latest task def
  before modifying. This chains R→R+1→R+2 correctly. The Python script always writes to
  `/tmp/td-fip-dev-new.json`, which is re-read on the next `register-task-definition` call.
- **Port 80 for fip-dev:** `fip` Cloud Map service uses port 80, not 8080. Other services use 8080.
- **Container name lookup:** Phase 3 dynamically fetches container names from task definitions
  rather than hardcoding them — safer across task def revisions.
- **Security group:** Phase 5 is manual/informational — it prints what Rhodey needs to do if
  the self-referencing rule is missing. It does NOT automatically add it (safer).

## How to verify

After Rhodey runs the setup script and ECS tasks restart:
```bash
cd ~/projects/fip
bash infra/cloud-map-verify.sh
```
