# QA Report: ADO#3168 — scheduled_tasks + scheduled_task_runs Migration
**Analyst:** Black Widow (Natasha)  
**Date:** 2026-05-10  
**Verdict:** ✅ PASS

---

## Deployment Under Test
- **Migration:** `20260510040449_AddScheduledTasksAndRuns`
- **Database:** `fait_dev`
- **Image:** `fred-chat:f1815a35`
- **Task Definition:** `fred-dev:155`

---

## Tests Run

### 1. Service Health Check ✅ PASS
```
running: 1
pending: 0
taskDef: arn:aws:ecs:us-east-1:742932328420:task-definition/fred-dev:155
```
Container running stable at expected revision. No pending tasks.

---

### 2. Schema Verification ✅ PASS

**`scheduled_tasks` table:**

| Field | Type | Null | Default |
|-------|------|------|---------|
| Id | char(36) | NO | — |
| UserId | char(36) | NO | — |
| ProjectId | char(36) | YES | NULL ✅ |
| Name | varchar(200) | NO | — |
| Prompt | text | NO | — |
| ScheduleType | enum('recurring','on_demand') | NO | NULL ✅ |
| CronExpression | varchar(100) | YES | NULL |
| NextRunAt | datetime(6) | YES | NULL |
| LastRunAt | datetime(6) | YES | NULL |
| LastRunStatus | enum('success','failed','cancelled') | YES | NULL ✅ |
| FailureCount | int | NO | 0 ✅ |
| AlertOnCompletion | tinyint(1) | NO | 0 |
| AlertOnFailure | tinyint(1) | NO | 1 ✅ |
| IsActive | tinyint(1) | NO | 1 |
| TaskMode | tinyint(1) | NO | 0 |
| CreatedAt | datetime(6) | NO | CURRENT_TIMESTAMP(6) |
| UpdatedAt | datetime(6) | NO | CURRENT_TIMESTAMP(6) |

**`scheduled_task_runs` table:**

| Field | Type | Null | Default |
|-------|------|------|---------|
| Id | char(36) | NO | — |
| TaskId | char(36) | NO | — |
| StartedAt | datetime(6) | NO | — |
| CompletedAt | datetime(6) | YES | NULL |
| Status | enum('success','failed','cancelled') | NO | NULL ✅ |
| Error | text | YES | NULL |
| ArtifactBlobPath | varchar(500) | YES | NULL |
| SandboxId | varchar(200) | YES | NULL |
| ResultSummary | varchar(500) | YES | NULL |

**Spec compliance check:**
- `ScheduleType` is `enum('recurring','on_demand')` NOT NULL ✅
- `LastRunStatus` is `enum('success','failed','cancelled')` NULL ✅
- `Status` in task_runs is `enum('success','failed','cancelled')` NOT NULL ✅
- `ProjectId` is nullable ✅
- `FailureCount` DEFAULT 0 ✅
- `AlertOnFailure` DEFAULT 1 ✅

All columns match spec exactly.

---

### 3. Migration History ✅ PASS
```
20260510040449_AddScheduledTasksAndRuns   ← confirmed present (most recent)
20260510014154_AddAvatarUrlToUserAssistantConfig
20260509000000_FaitDevConsolidation
```
Migration registered in `__EFMigrationsHistory`. Top of the stack, applied last.

---

### 4. CloudWatch — ERROR filter (last 10 min) ✅ PASS
```
[]
```
No ERROR-level log events. Clean startup.

---

### 5. CloudWatch — Exception filter (last 15 min) ✅ PASS
```
[]
```
No exceptions. No EF schema/init errors related to new tables.

---

## Summary

| Check | Result |
|-------|--------|
| ECS service health (running=1, pending=0, rev=155) | ✅ PASS |
| `scheduled_tasks` schema matches spec | ✅ PASS |
| `scheduled_task_runs` schema matches spec | ✅ PASS |
| Migration present in `__EFMigrationsHistory` | ✅ PASS |
| No ERROR logs (last 10 min) | ✅ PASS |
| No Exception logs (last 15 min) | ✅ PASS |

---

## Verdict: ✅ PASS

Migration deployed cleanly. Both tables exist with correct schema, all enum types and defaults match spec, no runtime errors, service stable at fred-dev:155.

---

_Black Widow — Trust nothing. Verify everything._
