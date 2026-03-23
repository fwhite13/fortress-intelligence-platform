# WI#920 — Close Opportunity Action Non-Functional

**Priority:** High
**Component:** FAMOS — Opportunity Workspace
**Repo:** fip monorepo (`fip/famos/`)

---

## What the User Sees

Clicking the red "Close" button in any Opportunity Workspace panel causes an immediate Blazor session disconnect. The app crashes/reconnects rather than showing the Close Opportunity dialog or performing the close action. No error message is displayed to the user.

## Expected Behavior

Clicking "Close" should open `CloseOpportunityDialog` with:
- A required `CloseReason` enum picker (NotQuoted / PriceTooHigh / LostToCompetitor / ClientDeclinedCoverage / PolicyLapsed / Other)
- An optional notes field
- Cancel and Confirm buttons
- On confirm: opportunity is marked closed with the selected reason, removed from active pipeline

Per Steve's spec (2026-03-19): structured close reasons are required — an opportunity cannot be closed without selecting a reason.

## Likely Cause

One of:
1. `CloseOpportunityAsync` handler crashing — possibly a missing DB column (`CloseReason` int nullable, `CloseNotes` longtext, `LastStageTransitionAt` datetime nullable) that was part of WI903 migrations
2. DB migration for `opportunities` table didn't run in dev environment
3. Bug in `CloseOpportunityAsync` call site

Clint should check whether the WI903 DB migrations (`CloseReason`, `CloseNotes`, `LastStageTransitionAt` on `opportunities` table) are present in the dev Aurora instance before Tony digs into code.

## Acceptance Criteria

1. Clicking "Close" on any opportunity opens `CloseOpportunityDialog` without crashing
2. Close reason is required — confirm button disabled until reason selected
3. On confirm: opportunity closed, removed from pipeline board, close reason persisted to DB
4. Cancel dismisses dialog, opportunity unchanged
5. No Blazor session disconnect under any close flow
