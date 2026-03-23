# WI#922 — Opportunity Workspace crashes when submissions exist

**Priority:** High — blocks quote scraping workflow
**Component:** FAMOS — Opportunity Workspace
**Repo:** fip monorepo (`fip/famos/`)

## What the User Sees

"Something went wrong — Can't convert Text to Int32" error page when opening an Opportunity Workspace for any opportunity that has carrier submissions in the DB.

Opportunities with zero submissions load fine. Adding even one submission record causes the crash.

## Reproduction

1. Insert a submission row for any opportunity:
   ```sql
   INSERT INTO submissions (Id, OpportunityId, CarrierName, Status, CoverageTypes, CreatedAt, UpdatedAt)
   VALUES (UUID(), '<opp-id>', 'Test Carrier', 0, 'AUTO', NOW(), NOW());
   ```
2. Open that opportunity in the workspace — crashes immediately with "Can't convert Text to Int32"

## Root Cause

`OpportunityService.GetByIdAsync` includes `.Include(o => o.Submissions)` which EF resolves. Something in the Submission entity or its navigation properties is causing a type conversion failure. The `Status` column in the DB is `longtext` but the entity maps it as `SubmissionStatus` enum (int). EF is likely trying to read a text value as Int32.

**Most likely fix:** The `Status` column in the `submissions` table needs to be `int` not `longtext`. Check the EF mapping in `FamOsDbContext.cs` — if `Status` is mapped as an enum without `.HasConversion()`, EF will try to read it as int from the DB column. The column type needs to match.

## Clint: Check first
```sql
DESCRIBE submissions; -- confirm Status column type
SHOW CREATE TABLE submissions;
```

If `Status` is `longtext` in the DB but the entity has `public SubmissionStatus Status` (enum/int), Tony needs to either:
- Run a migration to change the column type to `int`, OR  
- Add `.HasConversion<int>()` / `.HasConversion<string>()` in `FamOsDbContext` to match the current column type

## Acceptance Criteria
1. Opening an opportunity with carrier submissions does not crash
2. Submissions appear in the Underwriting Prep panel
3. Quote PDF Scraper is accessible for opportunities with submissions in Marketed stage
