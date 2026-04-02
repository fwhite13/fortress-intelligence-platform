# Pipeline State: NEXUS P1 Sprint

## Current Stage: BUILDING
## Risk Level: high (multi-epic, schema migration, new pages, service rewrites)
## Pipeline Path: full
## Review Cycles: 0

### WIs in scope
- #1518 — Multi-step submission wizard (NewSpecWizard.razor)
- #1519 — EF migration: submission_files junction table
- #1520 — Type-aware file processing (FileType enum, HtmlAgilityPack, PdfPig)
- #1522 — IMockupSectionizer interface + MockupSection model
- #1523 — Per-section screenshots (deferred if HtmlAgilityPack chosen)
- #1524 — SpecGenerationService multi-file update
- #1525 — SubmissionDetail.razor + SpecViewer
- #1526 — Export endpoints (MD/Word/PDF stub)
- #1527 — NexusReview.razor + inline edit
- #1528 — ISpecService.ApproveAsync + approve gate

### Key pre-flight findings
- PKs are int throughout (not CHAR(36)) — junction table must use int FKs to match
- Submission.MockupFileId is required int — must become nullable for narrative-only
- NexusSubmit.razor already at /nexus/new — wizard replaces it
- FileUploadZone is single-file today — needs multi-file upgrade

### Stage History
| Stage | Status | Agent | Started | Completed | Notes |
|-------|--------|-------|---------|-----------|-------|
| PLAN | ✅ DONE | Maria | 23:44 | 23:49 | Full codebase pre-read. Int PKs. Nullable MockupFileId needed. |
| BUILD | ✅ DONE | Tony | 23:49 | 00:03 | Commit 905a4fc. 0 errors. All epics. 4 flags for Clint. WI#1523 Deferred. |
| REVIEW | ⚠️ NEEDS-CHANGES | Clint | 00:03 | 00:16 | 4 issues: I1 vision timeout, I2 empty migration, I3 markdown render, I4 no service-layer role check on Approve |
| BUILD (cycle 2) | 🔄 ACTIVE | Tony | 00:16 | — | Fix I1–I4 only, no scope creep |
