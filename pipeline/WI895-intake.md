# WI#895 — FAM OS Sprint 4: Intake Form + Task Center

**Priority:** 2 (High)
**Tags:** famos; sprint4; intake; tasks

## Spec
Full spec at: `~/projects/fip/famos/FAMOS-SPRINT4-SPEC.md` (1,035 lines)

## Summary
Two features:
1. **Intake Form** — multi-step wizard in the IntakePanel to capture all Opportunity fields (account name, program, effective date, estimated premium, assigned ER, initial signal). Replaces current stub panel. Saves via new `SaveIntakeResponsesAsync` on LifecycleCommandService. Stores structured intake responses as JSON blob on Opportunity.
2. **Task Center** — full CRUD task management at `/tasks`. Tasks auto-generated on stage transitions via `StageTaskTemplates`. TaskService for CRUD. Task count badge in NavMenu. `AddTaskDialog` for manual task creation.

## Build
- Monorepo: `~/projects/fip/`
- Deploy to: `famos-dev` ECS service
- No schema changes beyond adding `IntakeResponsesJson` column to `opportunities`

## Standing dev approval in effect.
