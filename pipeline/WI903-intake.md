# WI#903 — FAM OS Sprint 5

## Priority: HIGH
## Spec: ~/projects/fip/famos/FAMOS-SPRINT5-SPEC.md (78KB — read fully before starting)

## Summary
7-part sprint. All parts in a single CC session. Sequential implementation — each part builds on the previous.

## Parts

### A — Submission Workflow
- New `Submission` entity (SubmissionStatus enum, CoverageTypes, SubmittedAt, QuoteResultJson, Notes)
- `CreateSubmissionAsync` + `UpdateSubmissionStatusAsync` in LifecycleCommandService (transactional)
- Stage gate: RouteToMarket validates `opp.Submissions.Any()`
- Full `UnderwritingPrepPanel` replacement: carrier dropdown, coverage types, submission list, blocked Route to Market button
- Full `MarketedPanel` replacement: expandable submission rows, inline status update, quote recording

### B — Quote Scraper Panel
- `QuoteScraperService.cs` — upload→S3 presigned PUT→submit→poll (same pattern as FORMS FortressProjectsClient)
- `QuoteScraperPanel.razor` — file picker, carrier selector, 60s poll loop with status, result display
- Creds from config: `FortressApi:Key`, `FortressApi:Secret`
- Project: `internal_quote_scraper_cataloger`, client: `internal`

### C — Structured Close Reasons
- `CloseReason` enum: NotQuoted/PriceTooHigh/LostToCompetitor/ClientDeclinedCoverage/PolicyLapsed/Other
- Full `CloseOpportunityDialog` replacement — enum picker + optional notes
- `CloseOpportunityAsync` signature change — BREAKING, Clint must verify all call sites

### D — Owner Initials on Cards
- `OpportunityCard.razor` — initials badge (email → "FW", UUID → first 2 chars)
- Red left-border tint for Urgent signal cards

### E — Lifecycle Aging Engine
- `AgingService.cs` background service, 15-min interval
- 6 aging rules → DominantSignal updates
- `LastStageTransitionAt` stamped on all lifecycle transitions
- DB migration: add `LastStageTransitionAt` to opportunities

### F — Dashboard Rebuild
- Full replacement of 4-card stub
- "Needs Attention" strip (urgent/at-risk, click-through)
- Pipeline distribution (CSS bar chart, no Chart.js)
- Recent Activity (last 5 Activity log entries)

### G — HubSpot Real Sync
- `HubSpotService.cs` replaces stub — searches by company name, PATCH deal stage on every lifecycle transition
- Sets `closedate` + probability on Bound/ClosedNotBound
- Conditional registration: real if `HubSpot:ServiceKey` set, stub if absent
- Stage map (8 FAM OS stages → HubSpot default pipeline IDs — see spec)

## DB Migrations Required
- `Submission` table (new)
- `opportunities`: add `CloseReason` (int nullable), `CloseNotes` (longtext), `LastStageTransitionAt` (datetime nullable)
- All Aurora MySQL compat — try/catch on error 1060, no IF NOT EXISTS

## Design System — MANDATORY
All new components must follow DESIGN-SYSTEM.md:
- No inline Variant/Color/Size on MudButton
- No inline Style="width:..." on inputs
- Use FamosIcons.* for all icons (not Icons.Material.*)
- No new CSS outside famos.css

## Clint Review Gates
- [ ] CloseOpportunityAsync signature change — verify all call sites updated
- [ ] HubSpot stage IDs match appsettings (appointmentscheduled, qualifiedtobuy, etc.)
- [ ] AgingService registered in Program.cs as hosted service
- [ ] Submission table FK to opportunities correctly defined
- [ ] DESIGN-SYSTEM.md checklist passed
- [ ] QA bypass works: /qa/status 200, all new pages accessible via X-QA-Bypass header

## Natasha QA (using bypass)
- /qa/status → 200 {qaBypass:true}
- Pipeline → submission panel visible on UW Prep stage card click
- Create submission → submission appears in list
- Route to Market blocked when no submissions, unblocked when one exists
- Dashboard: Needs Attention panel shows urgent opps, pipeline distribution visible
- Close opportunity → requires reason selection, can't close without reason
- Owner initials visible on pipeline cards (may be blank if OwnerUserId empty)
