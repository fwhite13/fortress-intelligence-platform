# DEPLOY Report — ADO#3239
## Harness Task Role: bedrock:Retrieve IAM Permission

**Date:** 2026-05-10  
**Engineer:** War Machine (James Rhodes) / DevOps subagent  
**Status:** ⛔ BLOCKED — Requires manual IAM action by Fred

---

## Summary

IAM policy update is required on the `fait-v2-task-role` to add `bedrock:Retrieve` and `bedrock:RetrieveAndGenerate` permissions. However, no available local AWS credential profile has `iam:PutRolePolicy` (or any IAM write) permissions on the FIP account (`742932328420`).

---

## Findings

### Task Role
- **Role name:** `fait-v2-task-role`
- **Role ARN:** `arn:aws:iam::742932328420:role/fait-v2-task-role`
- **Source:** `fait-v2-agent-harness:18` task definition

### Profiles Checked
| Profile | Account | IAM PutRolePolicy |
|---------|---------|-------------------|
| `fortress-tools-deployer` | 742932328420 | ❌ AccessDenied |
| `openclaw-bedrock` | 742932328420 | ❌ AccessDenied |
| `refuge-deployer` | 637131561301 | ❌ Wrong account + AccessDenied |

### Policy to Apply
**Policy name:** `BedrockKBRetrieve`  
**Type:** Inline role policy

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Sid": "BedrockKBRetrieve",
      "Effect": "Allow",
      "Action": [
        "bedrock:Retrieve",
        "bedrock:RetrieveAndGenerate"
      ],
      "Resource": "*"
    }
  ]
}
```

---

## Action Required (Manual — Fred)

Apply the policy via AWS Console or with an admin IAM user:

```bash
cat > /tmp/bedrock-retrieve-policy.json << 'EOF'
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Sid": "BedrockKBRetrieve",
      "Effect": "Allow",
      "Action": [
        "bedrock:Retrieve",
        "bedrock:RetrieveAndGenerate"
      ],
      "Resource": "*"
    }
  ]
}
EOF

aws iam put-role-policy \
  --role-name fait-v2-task-role \
  --policy-name BedrockKBRetrieve \
  --policy-document file:///tmp/bedrock-retrieve-policy.json \
  --region us-east-1
```

Or via AWS Console:
1. IAM → Roles → `fait-v2-task-role`
2. Add permissions → Create inline policy
3. Service: Bedrock, Actions: `Retrieve`, `RetrieveAndGenerate`, Resources: All
4. Name: `BedrockKBRetrieve`

### Verification (after applying)
```bash
aws iam simulate-principal-policy \
  --policy-source-arn arn:aws:iam::742932328420:role/fait-v2-task-role \
  --action-names bedrock:Retrieve \
  --resource-arns "*" \
  --query 'EvaluationResults[0].EvalDecision' --output text
# Expected: allowed
```

---

## No ECS Restart Required

IAM policy changes are effective immediately. No task definition update or ECS service restart is needed — the next harness task invocation will pick up the new permissions automatically.

---

## Cost Impact

None — IAM policy change only.
