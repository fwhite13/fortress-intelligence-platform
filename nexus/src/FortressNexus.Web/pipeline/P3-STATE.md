# Pipeline State: NEXUS Phase 3 — Draft Resume & Re-submission
**Epic:** #1644 | **WIs:** #1650–#1663 (14 stories) + 5 features + 1 epic = 20 total
**Started:** 2026-04-08 12:16 EDT
**Repo:** ~/projects/fip/nexus/src/FortressNexus.Web/

## Current Stage: DEPLOYING — WI #1657 | IN-REVIEW — WI #1659 + #1660

## Session Context
- Status "PendingReview" in spec = `SubmissionStatus.AwaitingReview` in code — use AwaitingReview in all subsequent WIs
## Risk Level: medium
## Pipeline Path: full (no approval gate — Fred tests live)
## Review Cycles: 0

---

## Execution Order

| # | WI | Title | Status |
|---|-----|-------|--------|
| 1 | #1662 | DB migration: Superseded status + Phase 3 schema | ✅ CLOSED |
| 2 | #1653 | NewSpecWizard ResumeSubmissionId param + on-init load | ✅ CLOSED |
| 3 | #1654 | Pre-populate narrative + existing files in resume mode | ✅ CLOSED |
| 4 | #1656 | _hasChanges change detection on Confirm step | ✅ CLOSED |
| 5 | #1657 | Superseded status + mark prior session on re-discovery | ✅ CLOSED |
| 6 | #1659 | Spec regen: new SpecDocument at Version+1 | 🔄 DEPLOYING |
| 7 | #1660 | Skip-regen path: Draft→AwaitingReview direct | 🔄 DEPLOYING |
| 8 | #1655 | Soft-delete files + narrative update on resume submit | 🔄 ACTIVE |
| 9 | #1661 | Live MudProgressLinear on Confirm during regen | ⏳ QUEUED |
| 10 | #1650 | SubmissionDetail Continue Submission CTA + preview | ⏳ QUEUED |
| 11 | #1651 | SubmissionDetail Delete Submission button + hard delete | ⏳ QUEUED |
| 12 | #1652 | SubmissionDetail Version History accordion | ⏳ QUEUED |
| 13 | #1658 | Show history toggle for superseded sessions | ⏳ QUEUED |
| 14 | #1663 | Natasha QA pass | ⏳ QUEUED |

---

## Stage History

| Stage | Status | WI | Agent | Started | Completed | Notes |
|-------|--------|----|-------|---------|-----------|-------|
| PLAN | ✅ DONE | All | Jarvis | 2026-04-08 | 2026-04-08 | Spec v1.3, WI map created |
| BUILD | ✅ DONE | #1662 | Tony | 12:16 EDT | 12:26 EDT | d42d0ed — migration + status constants |
| REVIEW | ↩️ NEEDS-CHANGES | #1662 | Clint | 12:26 EDT | 12:35 EDT | I1: constants unadopted — 17 literals in 3 files |
| BUILD | ✅ DONE | #1662 | Tony | 12:35 EDT | 12:38 EDT | 90fa325 — 17 literals replaced |
| REVIEW | ✅ DONE | #1662 | Clint | 12:38 EDT | 12:41 EDT | PASS — 0 raw literals, build clean |
| DEPLOY | ⚠️ PARTIAL | #1662 | Rhodey | 12:41 EDT | 12:58 EDT | nexus-web:16 healthy; migration failed — FK order bug |
| BUILD | ✅ DONE | #1662 | Tony | 12:58 EDT | 13:04 EDT | 109cf13 — FK drop/re-add fix |
| REVIEW | ✅ DONE | #1662 | Clint | 13:04 EDT | 13:08 EDT | PASS — FK names/order/symmetry all clean |
| DEPLOY | ✅ DONE | #1662 | Rhodey | 13:08 EDT | 13:21 EDT | nexus-web:17, migration APPLIED ✅ |
| CONFIRM | ✅ DONE | #1662 | Maria | 13:22 EDT | 13:22 EDT | WI Closed |

---

## Active Session Context

*(Updated as pipeline progresses)*
