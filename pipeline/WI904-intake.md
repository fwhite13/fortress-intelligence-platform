# WI#904 — FAM OS: Critical QA Failures (Fred Visual Inspection 2026-03-19)

## Priority: CRITICAL — blocks all further FAM OS work
## Type: Bug Fix / QA Investigation

## Context
Fred personally inspected famos.dev.fortressam.ai on 2026-03-19 ~19:00 EDT and found the following
issues AFTER multiple hard refreshes and incognito windows. Pipeline team claimed these were done.
This is a Clint + Natasha quality failure. Clint: investigate why these passed review.
Natasha: investigate why these passed QA with bypass active.

## Bug List (Fred's exact report)

### Dashboard / General
1. Titan logo still not centered in sidebar
2. Active Opportunities showing 0 (REGRESSION — was showing 67)
3. Pipeline and Tasks nav buttons do nothing on click
4. Pipeline distribution table shows 0 for all stages

### Pipeline page
5. New Opportunity button different style from buttons on Dashboard
6. Clicking New Opportunity button does nothing
7. Clicking opportunity cards does nothing
8. Fred has never been able to reach the Opportunity Workspace

### Task Center
9. "Task Center" page title has different font style than "Pipeline" or "Command Center"
10. Add Task button does nothing
11. (Filter box — PASS, this one is correct)

## Root Cause Investigation Required (Tony)
- Items 3, 6, 7, 10: All click handlers broken. Blazor interactivity likely not wired.
  Check: Does Routes.razor or any page have @rendermode set? In .NET 8 Blazor Web App,
  interactive server rendering requires EITHER global rendermode in Routes.razor OR
  @rendermode InteractiveServer on each page. Suspect this is missing.
- Item 2, 4: Active opportunities = 0 and pipeline distribution = 0 suggests DashboardSummary
  query is broken or returning empty. Check DashboardService — was the DB query broken by
  Sprint 5 schema changes (new columns, renamed columns)?
- Item 1: Logo centering CSS — verify .sb-logo uses display:flex + justify-content:center
- Item 5, 9: Button style and font inconsistency — verify famos-btn-outline-sm and page title
  CSS classes are applied correctly on Pipeline page and Task Center

## Natasha QA Failure Investigation
After fixing, Clint must document WHY Natasha signed off on non-functional click handlers.
If the bypass renders pages without a live Blazor circuit (static HTML only), clicks will
never work. Natasha may be testing rendered HTML but not verifying interactivity.
Suggest: Natasha QA checklist must include explicit click interaction tests, not just
visual/HTTP checks.

## Acceptance Criteria
- [ ] Clicking Pipeline nav item navigates to /pipeline
- [ ] Clicking Tasks nav item navigates to /tasks
- [ ] Clicking New Opportunity button opens create dialog
- [ ] Clicking any pipeline card navigates to /opportunity/{guid}
- [ ] Add Task button opens task dialog
- [ ] Active Opportunities count shows ~67 (non-zero)
- [ ] Pipeline distribution shows counts per stage (non-zero)
- [ ] Titan logo visually centered in sidebar white area
- [ ] All action buttons same visual style across Dashboard/Pipeline/TaskCenter
- [ ] Page titles consistent font across all pages
- [ ] All verified by Natasha with bypass AND confirmed by Fred before closing
