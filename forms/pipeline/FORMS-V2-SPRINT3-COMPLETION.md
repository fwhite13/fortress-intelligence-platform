# Pipeline Completion: FORMS v2 Sprint 3 — Cross-Reference Engine

## Outcome: DEPLOYED ✅ (with known follow-up)

**Final commit:** eedf26c  
**Deployed:** 2026-03-03 01:45 EST  
**URL:** https://forms.dev.fortressam.ai/

---

## What Shipped

- `FormFieldCode` entity — `FormFieldCodes` table, per-project unified field codes with carrier sources, panel IDs, sensitivity/shared/required flags
- `CrossReferenceService.CrossReferenceProjectAsync` — loads approved docs, builds Bedrock prompt from FormField records, parses JSON response, upsert (not delete-all) to FormFieldCodes, creates/updates project QuestionSet
- Bedrock SDK wired — `IAmazonBedrockRuntime` singleton registered, `us.anthropic.claude-sonnet-4-6` model
- `/projects/{id}/cross-reference` page — run button (disabled without approved docs), progress indicator, results table
- ProjectDetail — "Run Cross-Reference →" button when approved docs exist

---

## Pipeline Summary

- Build: 1 cycle + 1 fix cycle (Bedrock CLI → SDK, upsert)
- Review: 2 cycles
- Deploy: 1 attempt — SUCCEEDED
- QA: 1 attempt — WARN (feature passes, Form Library 400 noted)
- Total pipeline time: ~1h30m

---

## Known Follow-up: Form Library 400 (Not a Sprint 3 Regression)

**Root cause:** `FormLibrary.razor` uses `HttpClient → FormsController` pattern (v1 architecture). After multiple container restarts, Data Protection keys rotate (no `PersistKeysToDbContext` configured), invalidating browser sessions. The `UseAntiforgery()` middleware blocks requests from stale Blazor circuits, causing the server-side HttpClient call to fail.

**Fix items (log as follow-up):**
1. Configure `AddDataProtection().PersistKeysToDbContext<AppDbContext>()` so keys survive container restarts
2. Refactor `FormLibrary.razor` to use `IDbContextFactory<AppDbContext>` directly (like Sprint 1-3 pages) — removes the fragile HttpClient → controller dependency

**Severity:** Not a production blocker for Sprint 3 features. Fresh browser session clears the issue.

---

## Artifacts

- `pipeline/FORMS-V2-SPRINT3-BUILD-REPORT.md`
- `pipeline/FORMS-V2-SPRINT3-REVIEW-REPORT.md`
- `pipeline/FORMS-V2-SPRINT3-DEPLOY-REPORT.md`
- `pipeline/FORMS-V2-SPRINT3-QA-REPORT.md`
- `pipeline/FORMS-V2-SPRINT3-COMPLETION.md` (this file)
