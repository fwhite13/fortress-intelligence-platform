# Pipeline Completion: FORMS v2 Sprint 1 — Project Foundation

## Outcome: DEPLOYED ✅

**Final commit:** 9e2d9a5  
**Deployed:** 2026-03-03 00:37 EST  
**URL:** https://forms.dev.fortressam.ai/

---

## What Shipped

- **`FormProject` entity** — new `form_projects` table, groups documents under a named project with vertical/LOB
- **`FormLibrary.ProjectId`** — nullable FK to `FormProject` (v1 records unaffected)
- **`QuestionSet.ProjectId`** — nullable FK to `FormProject`
- **`/projects`** — Projects list page (empty state, create, delete)
- **`/projects/{id}`** — Project detail (Documents + Question Sets tabs, upload zone)
- **`ProjectDialog.razor`** — Create project dialog with Name, Vertical dropdown, Description
- **Nav updated** — "Projects" link added, Home routes to `/projects`
- **`FormsController`** — upload accepts optional `projectId`

---

## Pipeline Summary

- Build: 2 cycles (Tony) + 6 targeted fix cycles
- Review: 8 cycles (Clint)
- Deploy attempts: 5 (4 rollbacks due to DB init issues)
- QA: 3 attempts (2 FAIL, 1 PASS)
- Total pipeline time: ~1h45m

---

## Issues Surfaced & Fixed

1. `[Table]` attrs on existing entities broke DB name matching → reverted
2. `EnsureCreated` no-op on existing DB → explicit `CREATE TABLE IF NOT EXISTS`
3. `ALTER TABLE IF NOT EXISTS` syntax invalid in MySQL → removed `IF NOT EXISTS`
4. Table probe checking `forms` (nonexistent) → changed to `FormLibraries`
5. `catch (Exception)` swallowing all DB errors → narrowed to 1060/1061 only
6. Fire-and-forget `Task.Run` losing thrown exceptions → no change needed (outer catch + rethrow + LogCritical is visible in CloudWatch; ECS health check fails and rolls back)

---

## Key Lesson

**Never add `[Table("snake_case")]` to existing entities in a live DB** — the DB table name is already set by the first deployment's EF convention. Adding a `[Table]` attr renames the EF target without renaming the DB table. New entities only.

---

## Artifacts

- `pipeline/FORMS-V2-SPRINT1-BUILD-REPORT.md`
- `pipeline/FORMS-V2-SPRINT1-REVIEW-REPORT.md`
- `pipeline/FORMS-V2-SPRINT1-DEPLOY-REPORT-5.md`
- `pipeline/FORMS-V2-SPRINT1-QA-REPORT-3.md`
- `pipeline/FORMS-V2-SPRINT1-COMPLETION.md` (this file)
