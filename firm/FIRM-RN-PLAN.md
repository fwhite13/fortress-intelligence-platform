# FIRM/RN Implementation Plan
_Created: 2026-06-05 | Owner: Jarvis (no pipeline)_

---

## Overview

Two specs to implement, both touching `fip/firm` codebase and deploying to two independent AWS environments:

| Env | Cluster | Service | Task Def | AWS Account |
|-----|---------|---------|----------|-------------|
| FIRM (Fortress) | `fortress-tools-cluster` | `firm-web` | `firm-web:138` | `742932328420` |
| RN (Refuge) | `refuge-apps` | `rn-web` | `rn-web:16` | `637131561301` |

**Deployer creds:**
- FIRM: `source ~/projects/ai/projects/fortress_tools/.env.deployer`
- RN: `source ~/projects/refuge/.env.deployer`

**Build:** Both share one codebase. CodeBuild `rn-web-build` deploys RN automatically on push.
FIRM has its own CodeBuild project (`firm-web-build`) — or deploy manually via task def update.

---

## SPEC 1 — Hide FAIT/Teams/KB UI in MeetingDetail

### What changes

| Feature | FIRM | RN |
|---------|------|----|
| "Send to FAIT v2 Assistant" button | ❌ Remove | ❌ Remove |
| SharePanel — KB push section | ✅ Keep | ❌ Hide |
| SharePanel — Teams channel post | ❌ Hide | ❌ Hide |

### Files
- `firm/src/FortressIntelligenceRM.Web/Components/Pages/MeetingDetail.razor`
- `firm/src/FortressIntelligenceRM.Web/Components/Pages/SharePanel.razor`

### Tasks

- [ ] **S1-1** Inspect current `SharePanel.razor` — locate KB section and Teams section blocks
- [ ] **S1-2** Inspect current `MeetingDetail.razor` — locate `CanSendToFaitV2` block and SharePanel usage
- [ ] **S1-3** Add `ShowKbSection` + `ShowTeamsPost` params to `SharePanel.razor`; wrap each section in `@if`
- [ ] **S1-4** In `MeetingDetail.razor`: remove `CanSendToFaitV2` block; update `<SharePanel>` call with flags:
  ```razor
  <SharePanel MeetingId="@_meeting.Id"
              ShowKbSection="@(Branding.SuiteName != "RISE")"
              ShowTeamsPost="false" />
  ```
  If both flags are false (RN), suppress the outer `MudPaper` wrapper entirely.
- [ ] **S1-5** Local build verify — `dotnet build` passes clean
- [ ] **S1-6** Commit + push to main → triggers RN CodeBuild automatically
- [ ] **S1-7** Trigger FIRM CodeBuild (or manual task def update) → deploy to FIRM
- [ ] **S1-8** Verify FIRM: MeetingDetail shows KB section, no Teams, no FAIT button
- [ ] **S1-9** Verify RN: MeetingDetail shows neither KB, Teams, nor FAIT button

**Status:** ⬜ Not started

---

## SPEC 2 — Auto-Join: EventBridge Scheduled vpbot Launch

### Architecture
Future meeting added → `MeetingService.CreateMeetingAsync` → `EventBridgeSchedulerService.CreateScheduleAsync` → one-shot EventBridge Scheduler rule at `StartDatetime - 2min` → fires Lambda → `ECS.RunTask(vpbot)`.

Remove meeting → `DeleteScheduleAsync` → rule deleted (swallow not-found).

Feature flag: `Firm:AutoJoinEnabled` (default `false`) — infra goes in first, flag flipped after verification.

### AWS Resources (per environment)

#### FIRM (Fortress, account 742932328420)
- vpbot task def: `firm-vpbot:12`
- vpbot task role: `arn:aws:iam::742932328420:role/fortress-tools-ecs-task-role`
- vpbot exec role: `arn:aws:iam::742932328420:role/fortress-tools-ecs-execution-role`
- web task role: `arn:aws:iam::742932328420:role/fortress-tools-ecs-task-role` _(same role — will need scheduler perms added)_
- Lambda to create: `firm-autojoin`
- Lambda role: `firm-autojoin-role`
- Scheduler group: `firm-autojoin`
- Scheduler role: `firm-scheduler-role`

#### RN (Refuge, account 637131561301)
- vpbot task def: `rn-vpbot:3`
- vpbot task role: `arn:aws:iam::637131561301:role/rn-web-ecs-task-role`
- vpbot exec role: `arn:aws:iam::637131561301:role/rn-web-ecs-execution-role`
- web task role: `arn:aws:iam::637131561301:role/rn-web-ecs-task-role` _(same role — will need scheduler perms added)_
- Lambda to create: `rn-autojoin`
- Lambda role: `rn-autojoin-role`
- Scheduler group: `rn-autojoin`
- Scheduler role: `rn-scheduler-role`

### Tasks

#### Phase A — AWS Infra (both envs, no app changes needed yet)

- [ ] **S2-A1** Get FIRM vpbot VPC subnets + security group (from firm-vpbot task def / ECS service)
- [ ] **S2-A2** Get RN vpbot VPC subnets + security group (from rn-vpbot task def / ECS service)
- [ ] **S2-A3** Create `firm-autojoin-role` IAM role (Lambda trust) with `ecs:RunTask` + `iam:PassRole` on firm-vpbot roles
- [ ] **S2-A4** Create `firm-autojoin` Lambda (Python 3.12, ~20 lines, no VPC needed)
- [ ] **S2-A5** Create `firm-autojoin` EventBridge Scheduler group
- [ ] **S2-A6** Create `firm-scheduler-role` IAM role (Scheduler trust) with `lambda:InvokeFunction` on firm-autojoin
- [ ] **S2-A7** Add `scheduler:CreateSchedule` + `scheduler:DeleteSchedule` + `iam:PassRole` (on firm-scheduler-role) to `fortress-tools-ecs-task-role`
- [ ] **S2-A8** Repeat A3–A7 for RN (rn-autojoin-role, rn-autojoin Lambda, rn-autojoin group, rn-scheduler-role, rn-web-ecs-task-role perms)
- [ ] **S2-A9** Smoke-test both Lambdas with a dry-run invocation (pass dummy event, verify ECS RunTask is attempted)

#### Phase B — App Code

- [ ] **S2-B1** Add `Amazon.Scheduler` NuGet package to `FortressIntelligenceRM.Web.csproj`
- [ ] **S2-B2** Create `IAutoJoinSchedulerService` interface + `AutoJoinSchedulerService` implementation
  - `CreateScheduleAsync(meetingId, meetingUrl, startDatetime)` → EventBridge one-shot at `startDatetime - 2min`
  - `DeleteScheduleAsync(meetingId)` → delete schedule, swallow `ResourceNotFoundException`
  - Config: `Firm:AutoJoinEnabled`, `Firm:AutoJoinLambdaArn`, `Firm:AutoJoinSchedulerRoleArn`, `Firm:AutoJoinScheduleGroup`
  - No-op (return immediately) when `AutoJoinEnabled = false`
- [ ] **S2-B3** Register service in `Program.cs` (scoped + `IAmazonScheduler`)
- [ ] **S2-B4** Wire into `MeetingService.CreateMeetingAsync`:
  - If `StartDatetime != null && StartDatetime > UtcNow.AddMinutes(3)` → `CreateScheduleAsync`; set status = `Scheduled`
  - Otherwise → existing immediate-join path unchanged
- [ ] **S2-B5** Wire into `MeetingService.RemoveMeetingAsync`:
  - After DELETE → `DeleteScheduleAsync` (swallow not-found)
- [ ] **S2-B6** `Meetings.razor` — add "Scheduled for [time]" badge for `Scheduled` status meetings
- [ ] **S2-B7** Local build verify — `dotnet build` passes clean
- [ ] **S2-B8** Commit + push → RN CodeBuild deploys automatically (flag still false, no behavior change)
- [ ] **S2-B9** Trigger FIRM deploy

#### Phase C — Enable & Verify (after A + B complete)

- [ ] **S2-C1** Add env vars to `rn-web` task def: `Firm__AutoJoinEnabled=true`, `Firm__AutoJoinLambdaArn`, `Firm__AutoJoinSchedulerRoleArn`, `Firm__AutoJoinScheduleGroup=rn-autojoin`
- [ ] **S2-C2** Force new RN deployment with updated task def
- [ ] **S2-C3** Add same env vars to `firm-web` task def; force new FIRM deployment
- [ ] **S2-C4** End-to-end test: schedule a meeting 5+ min out → confirm EventBridge rule created → wait for T-2min → vpbot joins → meeting moves to Recording
- [ ] **S2-C5** Test removal: add scheduled meeting → remove it → confirm EventBridge rule deleted

**Status:** ⬜ Not started

---

## Deployment Order

```
SPEC 1 (code only, no infra)
  → S1-1 through S1-5 (inspect + edit + build)
  → S1-6 (push → RN auto-deploys)
  → S1-7 (trigger FIRM deploy)
  → S1-8/9 (verify both)

SPEC 2 Phase A (infra, no app changes)
  → S2-A1 through S2-A9 (AWS resources both envs)

SPEC 2 Phase B (app code, flag=false)
  → S2-B1 through S2-B9 (code + deploy, no behavior change)

SPEC 2 Phase C (enable)
  → S2-C1 through S2-C5 (flip flag + verify)
```

---

## Current Build State

- RN: CodeBuild `rn-web-build:a0389f9f` in progress (auth fix from earlier)
- FIRM: `firm-web:138` — current, no pending changes
- Main branch: `5449ac1b` (auth fix commit)

---

## Notes / Decisions

- `Branding.SuiteName != "RISE"` is the correct runtime check (matches existing pattern)
- FIRM and RN share the same ECS task role in each account — adding scheduler perms there covers both web + future services
- Lambda has no VPC needed — ECS API is a public endpoint; keep it simple
- `Firm:AutoJoinEnabled` flag means the code ships before infra is fully tested — zero risk to existing behavior
- FIRM CodeBuild: `fip-firm-build` (confirmed)
