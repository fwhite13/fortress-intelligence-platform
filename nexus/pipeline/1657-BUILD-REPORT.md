## Build Report — WI #1657 — Cycle 3

### Cycle 3 — HasMany/WithOne nav property fix

**Date:** 2026-04-08
**Branch:** main
**Status:** Complete

### What was built
Updated EF Core relationship config in `NexusDbContext.cs` from `HasOne/WithOne` (1:1) to `HasMany/WithOne` (1:many) for the `Submission → DiscoverySession` relationship. Updated the nav property on `Submission.cs` from `DiscoverySession?` (single) to `ICollection<DiscoverySession>` (collection). No migration delta — DB schema was already correct from Cycle 2.

### Files changed
- `src/FortressNexus.Web/Data/NexusDbContext.cs` — Updated `HasOne/WithOne` to `HasMany/WithOne` for Submission→DiscoverySession. Removed `.IsRequired(false)` (not applicable to HasMany).
- `src/FortressNexus.Web/Models/Entities/Submission.cs` — Changed nav property from `DiscoverySession? DiscoverySession` to `ICollection<DiscoverySession> DiscoverySessions`.

### Parallelization used
No — changes are sequential (DbContext references nav property type from entity).

### CC sessions run
1 — Sonnet, single-pass fix.

### Acceptance criteria verification
- [x] `HasMany/WithOne` in NexusDbContext — updated from HasOne/WithOne
- [x] `ICollection<DiscoverySession> DiscoverySessions` on Submission entity
- [x] No callers of old singular nav property outside migrations
- [x] `dotnet build` — 0 errors
- [x] Empty migration check — no schema changes detected

### Known edge cases / things Clint should scrutinize
- Migration snapshot files still reference old HasOne/WithOne — this is expected; migration snapshots reflect historical state and are not affected by model-only changes when no new migration is added.
- Service layer (`DiscoveryService`) queries by `SubmissionId` directly, not via nav property — no service changes needed.

### How to test locally
1. `cd ~/projects/fip/nexus/src/FortressNexus.Web && dotnet build` — should be 0 errors
2. Run the app and trigger a new discovery session on a submission — should create a second session rather than failing on uniqueness constraint.
