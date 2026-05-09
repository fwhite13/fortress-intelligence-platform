# FAIT v2 Deploy Runbook

Operational procedures for deploying and maintaining FAIT v2 on AWS ECS.

---

## Post-Harness Deploy Steps

**When:** After every `fait-v2-agent-harness` ECS task definition registration (new revision).

**Why:** Active user sessions store the Fargate task ARN pointing to the old task def revision. After a new revision is registered, those sessions are stale and must be invalidated so the next agent spawn picks up the new task def.

**Script:** `scripts/post-harness-deploy.sh`

### Steps

1. Ensure `FAIT_DB_PASS` is available (retrieve from AWS Secrets Manager or a secure vault — do **not** store in `.env.deployer`).

2. Run the invalidation script:
   ```bash
   cd /home/fredw/projects/fip/fait-v2
   FAIT_DB_PASS=<password> ./scripts/post-harness-deploy.sh
   ```

3. Verify output:
   ```
   [post-harness-deploy] Connecting to fortress-ai.c89acukue4d5.us-east-1.rds.amazonaws.com/fait_v2_dev as fait_app...
   Session invalidation complete. N rows updated.
   ```

4. If `N = 0`, no sessions were active — this is normal if no users were connected at deploy time.

5. If the script errors with a DB connection failure, verify:
   - `FAIT_DB_PASS` is correct
   - The host machine has network access to the Aurora endpoint (may require VPN or bastion)

### What the Script Does

Runs this SQL against `fait_v2_dev`:
```sql
UPDATE user_sessions
SET fargate_status='Stopped', ended_at=NOW(), updated_at=NOW()
WHERE fargate_status IN ('Running','Starting') AND ended_at IS NULL;
```

This marks all in-flight sessions as stopped so the application correctly spawns new Fargate tasks using the updated task definition.

---
