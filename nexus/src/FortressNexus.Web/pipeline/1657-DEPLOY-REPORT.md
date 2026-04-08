# Deploy Report — WI #1657 — Superseded Session + Re-Discovery

**Date:** 2026-04-08  
**Deployed by:** War Machine (Rhodey / devops)  
**Session:** nexus-p3-wi1657-deploy

---

## Summary

| Field | Value |
|---|---|
| **Work Item** | FAIT #1657 |
| **App** | nexus-web |
| **Cluster** | fortress-tools-cluster |
| **Task Def (before)** | nexus-web:19 |
| **Task Def (after)** | nexus-web:20 |
| **Image** | `742932328420.dkr.ecr.us-east-1.amazonaws.com/nexus-web:681e5d2cc79b564920cb1874df94cb50c91e5807` |
| **Image Digest** | `sha256:f01df7516ba04edba02bc6b3710867d3031680bbef7d075da65cfc43e5a41b3f` |
| **Commit** | `681e5d2` — feat(nexus#1657): fix HasOne/WithOne to HasMany/WithOne for Submission→DiscoverySession |
| **CodeBuild Build** | `fip-nexus-build:2ecb5826-f07f-4e81-ba55-d45ef278312a` (Build #18) |
| **Build Duration** | ~1m 20s (14:44:01 → 14:45:21 EDT) |
| **ECS Deployment** | SUCCEEDED — 1/1 running, PRIMARY |
| **Health** | 403 (Entra auth wall — expected) |
| **Migration** | `DropDiscoverySessionsUniqueSubmissionIndex` — APPLIED (manual, see note) |

---

## Deploy Steps

| # | Step | Result |
|---|---|---|
| 1 | Pre-flight: ECS service nexus-web:19, 1/1 running | ✅ |
| 2 | Verified HEAD = `681e5d2` on main | ✅ |
| 3 | CodeBuild `fip-nexus-build` started — Build #18 | ✅ |
| 4 | CodeBuild SUCCEEDED (~1m 20s) | ✅ |
| 5 | New image pushed: `681e5d2cc79b564920cb1874df94cb50c91e5807` | ✅ |
| 6 | Task def `nexus-web:20` registered | ✅ |
| 7 | ECS service updated → nexus-web:20, force-new-deployment | ✅ |
| 8 | ECS wait services-stable — converged 1/1 PRIMARY | ✅ |
| 9 | Migration `DropDiscoverySessionsUniqueSubmissionIndex` — APPLIED (manual) | ✅ ⚠️ |
| 10 | Health check: `https://nexus.fortressam.ai/` — 403 | ✅ |

---

## Migration Note — Manual Application Required

**Migration:** `20260408180000_DropDiscoverySessionsUniqueSubmissionIndex`

**Root cause:** The EF Core migration calls `DropIndex("IX_discovery_sessions_submission_id")`, but MySQL blocked the DROP because a foreign key constraint (`FK_discovery_sessions_submissions_submission_id`) was backed by that index. EF did not know about this FK because it's not explicitly modeled in the EF navigation from the `discovery_sessions` side with `HasForeignKey`.

**Error logged:**
```
MySqlConnector.MySqlException: Cannot drop index 'IX_discovery_sessions_submission_id': needed in a foreign key constraint
```

**Manual fix applied directly to DB:**
```sql
-- 1. Drop FK that references the index
ALTER TABLE `discovery_sessions` DROP FOREIGN KEY `FK_discovery_sessions_submissions_submission_id`;

-- 2. Drop unique index (now unblocked)
ALTER TABLE `discovery_sessions` DROP INDEX `IX_discovery_sessions_submission_id`;

-- 3. Create non-unique index (the intended result)
CREATE INDEX `IX_discovery_sessions_submission_id` ON `discovery_sessions` (`submission_id`);

-- 4. Re-add FK
ALTER TABLE `discovery_sessions` ADD CONSTRAINT `FK_discovery_sessions_submissions_submission_id`
  FOREIGN KEY (`submission_id`) REFERENCES `submissions` (`id`) ON DELETE CASCADE;

-- 5. Mark migration as applied
INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260408180000_DropDiscoverySessionsUniqueSubmissionIndex', '8.0.13');
```

**Verified post-apply:**
- `IX_discovery_sessions_submission_id` → `Non_unique=1` ✅
- FK `FK_discovery_sessions_submissions_submission_id` → intact ✅
- `__EFMigrationsHistory` → migration recorded ✅

**Action required for dev:** The EF migration file should be updated to explicitly drop and re-add the FK around the index change, so future deploys don't require manual intervention.

---

## Schema State (post-deploy)

```
discovery_sessions.submission_id:
  - Index: IX_discovery_sessions_submission_id (NON-UNIQUE)  ← changed from UNIQUE
  - FK:    FK_discovery_sessions_submissions_submission_id → submissions.id ON DELETE CASCADE
```

---

## Rollback

```bash
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service nexus-web \
  --task-definition nexus-web:19 \
  --force-new-deployment \
  --profile fortress-tools-deployer \
  --region us-east-1
```
Note: Schema change (non-unique index) is backward-compatible with nexus-web:19 code.

---

## Migrations Applied This Deploy

| Migration | Status |
|---|---|
| `20260408180000_DropDiscoverySessionsUniqueSubmissionIndex` | ✅ Applied (manual) |
