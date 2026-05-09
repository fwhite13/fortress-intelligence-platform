# ADO#3123 — Schema Consolidation: FAIT v2 → fait_dev

## Context
FAIT v2 is the replacement for FAIT v1. Decision: consolidate onto shared `fait_dev` database. The current `FORTRESS_DB_NAME=fait_v2_dev` is WRONG — v2 should use `fait_dev`.

Working directories:
- V2 source: `/home/fredw/projects/fip/fait-v2`
- V1 source: `/home/fredw/projects/fip/fait`

## TASK 1: Full Table Audit

Read v2's DbContext:
- `/home/fredw/projects/fip/fait-v2/src/FortressAI.V2.Web/Data/FaitV2DbContext.cs`
- List ALL DbSet<> properties and any ToTable() overrides
- Also check the ModelSnapshot for full table list: `/home/fredw/projects/fip/fait-v2/src/FortressAI.V2.Web/Data/Migrations/FaitV2DbContextModelSnapshot.cs`

Read v1's DbContext:
- `/home/fredw/projects/fip/fait/src/FortressAI.Web/Data/AppDbContext.cs`
- List ALL DbSet<> and ToTable() entries

For each v2 table, classify:
- **COMPATIBLE** — same table name, compatible schema (v2 is same or superset)
- **CONFLICT** — same table name, incompatible schema (type mismatches, PK type differences)
- **V2-ONLY** — exists in v2, not in v1 (needs EF migration to create in fait_dev)
- **V1-ONLY** — exists in v1, not in v2 (informational only, no action needed)

Known conflicts to look for:
- `kb_entries`: v1 int PKs vs v2 varchar(36) GUID PKs
- `kb_teams`: v1 int PKs vs v2 varchar(36) GUID PKs  
- `kb_team_members`: v1 int FKs vs v2 varchar FKs
- `users`: possibly different structure

Also read the v2 model entity classes to understand PK/FK types:
- Find all entity model files under `/home/fredw/projects/fip/fait-v2/src/FortressAI.V2.Web/Models/` or `Data/Entities/` or similar
- Look at the migration files to understand what changes were made

Also read v1 entity models under `/home/fredw/projects/fip/fait/src/FortressAI.Web/Models/` or similar.

## TASK 2: Check DB connection config

Read these files:
- `/home/fredw/projects/fip/fait-v2/src/FortressAI.V2.Web/appsettings.json`
- `/home/fredw/projects/fip/fait-v2/src/FortressAI.V2.Web/appsettings.Development.json` (if exists)
- `/home/fredw/projects/fip/fait-v2/src/FortressAI.V2.Web/Program.cs`

Find:
- Where `FORTRESS_DB_NAME` env var is read
- What the current connection string template looks like
- How the DB context is registered

## TASK 3: Analyze migration history

List migration files in v2:
- `/home/fredw/projects/fip/fait-v2/src/FortressAI.V2.Web/Data/Migrations/`

List migration files in v1:
- `/home/fredw/projects/fip/fait/src/FortressAI.Web/Migrations/`

For each CONFLICT table in v2, determine if a migration already handles the schema difference, or if we need to write a new migration.

## TASK 4: Reconciliation Plan + Implementation

For each CONFLICT table, decide:

### kb_entries / kb_teams / kb_team_members conflict (GUID vs int PKs):
- Read the v2 entity classes for these tables  
- Read the v1 entity classes / migration that created them
- DECISION FRAMEWORK: Since v2 is the future, v2's GUID PKs should be used. This means updating `fait_dev` to use GUID PKs for these tables.
- However, if `fait_dev` already has data with int PKs, we need a migration strategy
- Check if there's existing migration history in v1 for these tables

### For V2-ONLY tables:
- Write EF migrations that create them in fait_dev

### Implementation steps:
1. Check if v2 has any hardcoded `fait_v2_dev` references (not env vars) that need updating
2. Review any migration that targets a specific DB name
3. Write any needed new migrations for CONFLICT resolution or V2-ONLY table creation
4. Run `dotnet build` from `/home/fredw/projects/fip/fait-v2/src/FortressAI.V2.Web` — fix any errors

## TASK 5: Write the report

Create `/home/fredw/projects/fip/fait-v2/pipeline/ADO3123-SCHEMA-REPORT.md` with:

```markdown
# ADO#3123 — Schema Consolidation Report

## Summary
- Total v2 tables: N
- COMPATIBLE: N
- CONFLICT: N  
- V2-ONLY: N
- V1-ONLY: N

## Full Table Classification
[table with name, classification, notes]

## Conflict Resolution Decisions
[for each CONFLICT: what was decided and why]

## V2-ONLY Tables — Migrations Required
[list]

## ECS Task Definition Change Required
- `FORTRESS_DB_NAME` env var: change from `fait_v2_dev` → `fait_dev`
- No code change needed (env var based)

## Migrations Created/Modified
[list with file names]

## Risks & Data Loss Concerns
[honest assessment]

## Build Verification
[dotnet build result]
```

## TASK 6: Final steps

After writing the report:
1. Run `dotnet build` from `/home/fredw/projects/fip/fait-v2/src/FortressAI.V2.Web` and verify 0 errors
2. Stage changed files: `cd /home/fredw/projects/fip/fait-v2 && git add -A`
3. Commit: `git commit -m "feat(fait#3123): schema consolidation — v2 EF models and migrations aligned for fait_dev"`
   - If nothing changed (only docs), use `git add pipeline/` and commit just the report
4. Post ADO comment: `mcporter call devops.add_comment --args '{"project": "Fortress", "id": 3123, "text": "Schema audit complete. COMPATIBLE: N, CONFLICT: N, V2-ONLY: N. [brief decisions summary]"}'`

## Important Notes
- DO NOT run `dotnet ef database update` — Rhodey handles all DB migrations during deploy
- DO NOT change ECS task definition — document what needs to change, Rhodey does the deploy
- DO NOT hardcode connection strings — env var pattern must be preserved
- The pipeline report goes in `/home/fredw/projects/fip/fait-v2/pipeline/ADO3123-SCHEMA-REPORT.md`
- The pipeline build report goes in `/home/fredw/.openclaw/agents/pipeline-manager/pipeline/ADO3123-BUILD-REPORT.md`
