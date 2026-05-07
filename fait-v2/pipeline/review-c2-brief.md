# Hawkeye Review Brief — ADO#2844, Cycle 2

You are performing a targeted adversarial code review for cycle 2 of ADO#2844.
Prior cycle issued NEEDS-CHANGES with C1 (diagnostic flags), N1 (CancellationToken), N2 (GUID guard), N3 (unique constraint migration).

## Files to review

1. `src/FortressAI.V2.Web/Services/UserProvisioningService.cs` — C1, N1, N2 fixes
2. `src/FortressAI.V2.Web/Data/Migrations/20260507010358_AddMemoryTopicsUniqueConstraint.cs` — N3 migration
3. `src/FortressAI.V2.Web/Data/Migrations/20260507010358_AddMemoryTopicsUniqueConstraint.Designer.cs` — N3 Designer snapshot

## Check each fix exactly as described below:

### C1 — Per-step diagnostic flags in ProvisionAsync
Verify that `UserProvisioningService.cs` has:
- Four boolean flags: `s3Complete`, `pgComplete`, `auroraAddComplete`, `seedComplete`
- Each flag is set to `true` ONLY after its respective step fully completes (not before)
  - `s3Complete = true` after ALL 4 S3 files are written (after the foreach loop)
  - `pgComplete = true` after `CreatePgSchemaAsync` returns successfully
  - `auroraAddComplete = true` after `_db.MainAssistants.Add(assistant)` executes
  - `seedComplete = true` after the memory_topics seeding loop completes
- The `FailedStep` ternary uses these four flags in this exact order:
  `!s3Complete ? "s3-write" : !pgComplete ? "pg-schema" : !auroraAddComplete ? "aurora-record" : !seedComplete ? "memory-topics-seed" : "aurora-save"`
- There is NO remaining `auroraRecordCreated` flag used in the ternary
- Report exact line numbers for flags and ternary

### N1 — CancellationToken in DropPgSchemaAsync
Verify that `DropPgSchemaAsync`:
- Signature includes `CancellationToken ct = default`
- `OpenAsync(ct)` — ct passed
- `ExecuteNonQueryAsync(ct)` — ct passed
- The call site in the catch/rollback block passes `ct` (or a valid token)
- Report exact line numbers

### N2 — Guid.TryParse guard in ProvisionAsync
Verify that `ProvisionAsync`:
- Has a `Guid.TryParse(userId, out _)` guard at the TOP of the method
- The guard throws `ArgumentException` if parse fails
- It appears BEFORE any DB access (before the `_db.Users.FirstOrDefaultAsync` call)
- Report exact line numbers

### N3 — Migration AddMemoryTopicsUniqueConstraint
This is the most critical check. Read the migration file carefully:
`20260507010358_AddMemoryTopicsUniqueConstraint.cs`

Verify:
- The `Up()` method body is NOT empty — it must contain a `migrationBuilder.CreateIndex()` call (or equivalent) that creates a unique index on `memory_topics(user_id, topic_slug)`
- The `Down()` method must contain the corresponding `DropIndex()` call
- The Designer.cs snapshot shows `HasIndex("UserId", "TopicSlug").IsUnique()` for MemoryTopic

**If `Up()` is empty, this is a CRITICAL DEFECT.** An empty `Up()` means the migration file exists but will NOT apply the constraint to the database. The snapshot may show it in the model, but the actual `ALTER TABLE` / `CREATE INDEX` was never generated. This is a common EF scaffolding issue when `dotnet ef migrations add` was run but the index was already in the model snapshot from a previous migration (or the migration was created manually without running the EF tooling properly).

### Scope check
Confirm that ONLY these files were changed between commits 5754984 and 09b6ce1 (excluding pipeline docs):
- `Services/UserProvisioningService.cs`
- `Data/Migrations/20260507010358_AddMemoryTopicsUniqueConstraint.cs`
- `Data/Migrations/20260507010358_AddMemoryTopicsUniqueConstraint.Designer.cs`
- `Data/Migrations/FaitV2DbContextModelSnapshot.cs`

## Output format

For each check: ✅ PASS or ❌ FAIL with specific evidence (file + line number).
Final summary: list of all checks and overall verdict (PASS / NEEDS-CHANGES / FAIL).
