# WI#982 — Dashboard: KPI Cards + Pipeline Stage Bar Chart

## Type
Feature

## Source
TIG mock-up (TIG_Portal_v1.html) — Lauren Williams

## Description
The dashboard currently lacks the KPI summary cards and pipeline-by-stage bar chart visible in the TIG mock-up. These are the first things a user sees on login.

## Expected Behavior

### KPI Cards (4 cards in a row)
- **Active Opps** — count of opportunities across all pipeline stages, with MTD delta badge
- **DGT Pending** — count of accounts awaiting data collection (Intake/Prospect stage), with trend badge
- **At Higg / Quoted** — count of opps in Submitted + Marketing + Quoted stages, with delta badge
- **Est. Pipeline Premium** — sum of estimated annual premium across all active opps (format: $342K), with % trend badge (up arrow)
- Cards have a left-side color accent bar (navy/red/green/amber per card)
- KPI values use Fraunces serif font (large, editorial)

### Pipeline by Stage Bar Chart
- Horizontal bar chart — one row per pipeline stage
- Each row: stage label (right-aligned, 80px) | bar (fills proportionally) | count badge | est. premium (right-aligned)
- Stage fill colors match the stage dot colors (prospect=gray, DGT Active=blue, Submitted=purple, Marketing=amber, Quoted=sky, Proposal=pink, Bound=green)
- Bars are clickable — navigate to Pipeline view filtered to that stage
- "Full View" button links to Pipeline page

### Stale Deal Alerts (card on dashboard)
- Lists accounts that have been stale (no movement) with age indicators
- Two severity levels: warn (amber, >14 days) and urgent (red, >21 days or near expiry)
- Each item: company name, stage, reason, days stale badge
- Clickable — opens account side panel

## Mock Data Requirements
- Ensure enough mock opportunities exist across stages to make bars and KPIs look meaningful
- Minimum: 20+ opportunities spread across at least 5 stages
- Include at least 3–4 stale items (mix of warn and urgent)

## Notes
- TIG mock-up shows: 24 active opps, 8 DGT pending, 9 at Higg/Quoted, $342K est. premium
- Font: Fraunces serif for KPI numbers (already in mock-up, add to FAMOS if not present)
- Keep layout consistent with existing FAMOS card/grid system
