# Pipeline State: NEXUS Phase 3 — Draft Resume & Re-submission
**Epic:** #1644 | **WIs:** #1650–#1663 (14 stories) + 5 features + 1 epic = 20 total
**Started:** 2026-04-08 12:16 EDT
**Repo:** ~/projects/fip/nexus/src/FortressNexus.Web/

## Current Stage: BUILDING (cycle 3) — WI #1662
## Risk Level: medium
## Pipeline Path: full (no approval gate — Fred tests live)
## Review Cycles: 0

---

## Execution Order

| # | WI | Title | Status |
|---|-----|-------|--------|
| 1 | #1662 | DB migration: Superseded status + Phase 3 schema | 🔄 ACTIVE |
| 2 | #1653 | NewSpecWizard ResumeSubmissionId param + on-init load | ⏳ QUEUED |
| 3 | #1654 | Pre-populate narrative + existing files in resume mode | ⏳ QUEUED |
| 4 | #1656 | _hasChanges change detection on Confirm step | ⏳ QUEUED |
| 5 | #1657 | Superseded status + mark prior session on re-discovery | ⏳ QUEUED |
| 6 | #1659 | Spec regen: new SpecDocument at Version+1 | ⏳ QUEUED |
| 7 | #1660 | Skip-regen path: Draft→PendingReview direct | ⏳ QUEUED |
| 8 | #1655 | Soft-delete files + narrative update on resume submit | ⏳ QUEUED |
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
| BUILD | 🔄 ACTIVE | #1662 | Tony | 12:58 EDT | — | Cycle 3 — fix FK drop/re-add in migration |

---

## Active Session Context

*(Updated as pipeline progresses)*
