# Adversarial Code Review Brief — ADO#2844
# UserProvisioningService — S3 workspace, PG schema, Aurora records

You are doing an adversarial code review. Be skeptical. Find real bugs, not superficial style issues.

## Task
Verify the UserProvisioningService implementation for ADO#2844 is:
1. Correct and complete per the 7-step sequence
2. Idempotent throughout
3. Has proper rollback
4. Has no hardcoded secrets
5. No Cognito or Azure Blob references
6. All config reads from IConfiguration correctly

## Files to analyze (read them all):
- /home/fredw/projects/fip/fait-v2/src/FortressAI.V2.Web/Services/UserProvisioningService.cs
- /home/fredw/projects/fip/fait-v2/src/FortressAI.V2.Web/Services/IUserProvisioningService.cs
- /home/fredw/projects/fip/fait-v2/src/FortressAI.V2.Web/Services/Exceptions/ProvisioningException.cs
- /home/fredw/projects/fip/fait-v2/src/FortressAI.V2.Web/appsettings.json
- /home/fredw/projects/fip/fait-v2/src/FortressAI.V2.Web/Program.cs
- /home/fredw/projects/fip/fait-v2/src/FortressAI.V2.Web/FortressAI.V2.Web.csproj

## Critical checklist — verify each with evidence from the code:

### CRITICAL CHECKS:
1. IDEMPOTENCY: Does the code check `OnboardingCompletedAt != null` at the TOP and return early? Show the exact lines.

2. 7-STEP SEQUENCE: Are all 7 steps present?
   - Step 1: Idempotency check
   - Step 2: Upsert users record
   - Step 3: Write 4 S3 files (SOUL.md, USER.md, AGENTS.md, MEMORY.md)
   - Step 4: Create PG schema + tables + index
   - Step 5: Add main_assistants Aurora record
   - Step 6: Seed 4 memory_topics rows (soul, user, memory, agents)
   - Step 7: Set OnboardingCompletedAt + SaveChangesAsync

3. S3 BUCKET: Is it read from IConfiguration["AWS:WorkspaceBucket"]? Is it ever hardcoded? Show the line.

4. PG CONNECTION STRING: Is it read from IConfiguration.GetConnectionString("PostgresConnection")? Never hardcoded?

5. PutObjectAsync: Are all S3 writes using PutObjectAsync (not PutObject or other variants)?

6. IDEMPOTENT DDL: Does the PG schema creation use:
   - CREATE SCHEMA IF NOT EXISTS
   - CREATE TABLE IF NOT EXISTS (for BOTH memory_chunks AND memory_topics)
   - CREATE EXTENSION IF NOT EXISTS vector
   - CREATE INDEX IF NOT EXISTS

7. PGVECTOR: Does memory_chunks have `embedding vector(1536)` column?

8. ROLLBACK LOGIC:
   - If PG fails (after S3 succeeds): are S3 objects deleted?
   - If Aurora SaveChanges fails (after PG succeeds): is PG schema dropped AND S3 deleted?
   - IMPORTANT: Check the ORDERING of rollback — does it drop PG before deleting S3 or vice versa?

9. HARDCODED CREDENTIALS: Search ALL files for any hardcoded passwords, access keys, secrets. 
   - appsettings.json: PostgresConnection password must be PLACEHOLDER only
   - No AWS credentials hardcoded anywhere

10. COGNITO: Any reference to Cognito anywhere in these files? (Flag if found)

11. AZURE BLOB: Any reference to Azure Blob Storage, BlobServiceClient, WindowsAzure.Storage, Azure.Storage.Blobs? (Flag if found)

12. ProvisioningException: Does it carry BOTH UserId AND FailedStep properties?

13. GetPgSchemaName: Does it replace hyphens (-) with underscores (_)?

### IMPORTANT CHECKS:
14. S3 CONTENT SUBSTITUTION: In the S3 file templates, are {DisplayName} and {Email} placeholders actually substituted at runtime? Check each template's Replace() calls. Are any placeholders left un-substituted?

15. Program.cs: Is AddAWSService<IAmazonS3>() present?

16. Program.cs: Is AddScoped<IUserProvisioningService, UserProvisioningService>() present?

17. csproj: Is AWSSDK.S3 present? Is Npgsql present?

18. EF CHANGETRACKER: During rollback, is ChangeTracker.Entries() detached (set to EntityState.Detached)? 

19. MEMORY TOPICS COUNT: Are exactly 4 DefaultTopics defined? Slugs: soul, user, memory, agents?

### LOGIC / EDGE CASE CHECKS:
20. STEP IDENTIFICATION IN EXCEPTION: When throwing ProvisioningException, does the failedStep argument correctly identify the actual failed step? Trace the logic of the ternary/conditional used. Is there an off-by-one in step identification?

21. ROLLBACK ORDER ISSUE: The spec says "S3 objects deleted if PG fails" and "PG schema dropped if Aurora save fails". In the actual catch block, is rollback done for BOTH conditions (i.e., if Aurora save fails, does it roll back BOTH PG AND S3)?

22. PG SCHEMA SQL INJECTION: The schemaName is built from userId. The schemaName is double-quoted in SQL. Is the quoting correct to prevent injection? What characters could break out of double-quotes?

23. MISSING CancellationToken in DropPgSchemaAsync: Does DropPgSchemaAsync accept and use CancellationToken? If not, flag it.

24. STEP 6 IDEMPOTENCY vs STEP 7 ORDERING: memory_topics are seeded in step 6, but the EF SaveChangesAsync is in step 7. Does this mean the AnyAsync check in step 6 will always return false on first run (because nothing is saved yet)? Is there a TOCTOU issue here?

## Report format:
For each check: state PASS/FAIL/WARN and cite exact lines or quote the code.
At the end: give an overall verdict of PASS, NEEDS-CHANGES, or FAIL with a summary.
