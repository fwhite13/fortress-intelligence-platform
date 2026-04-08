# Review Report — FAIT WI #1667 — KB Notes not retrievable

**Verdict: ✅ PASS**
**Cycle:** 1 of 2
**Reviewer:** Hawkeye (code-reviewer)
**Date:** 2026-04-08
**Commit:** `163f4c3`

---

## Spec Compliance Check

**Fix described:** `ForgeService.CreateEntryAsync()` only wrote notes to MySQL — never to S3. Bedrock cannot retrieve from MySQL. Fix adds S3 sync (upload on create/update, delete on delete) plus ingestion trigger via `KbDocumentService.StartIngestionAsync()`.

**Files modified:**
- `fait/src/FortressAI.Web/Services/ForgeService.cs` — ✅ correct, only file that needed changes per spec

**Acceptance criteria:**
- [x] Notes written to S3 on create ✅
- [x] Notes overwritten on update (same key) ✅
- [x] Notes deleted from S3 on entry delete ✅
- [x] Ingestion triggered after each S3 write ✅
- [x] S3 failures are non-fatal (DB write always completes) ✅

**Spec compliance verdict:** ✅ COMPLIANT

---

## Consistency Audit

**Files cross-referenced:**
- `ForgeService.cs` ↔ `KbDocumentService.cs` — bucket name, S3 prefix pattern ✅
- `ForgeService.cs` ↔ `KnowledgeBaseService.cs` — metadata key names (`ownerId`, `teamId`) ✅
- `ForgeService.cs` ↔ `Program.cs` — DI registration for `IAmazonS3` ✅

**S3 prefix alignment verified:**

| Tier | ForgeService key | KbDocumentService key | Status |
|------|-----------------|----------------------|--------|
| Personal | `kb-docs/personal/{userId}/note-{id}.txt` | `kb-docs/personal/{userId}/{filename}` | ✅ same prefix |
| Team | `kb-docs/teams/{teamId}/note-{id}.txt` | `kb-docs/teams/{teamId}/{filename}` | ✅ same prefix |
| Developer | `kb-docs/dev/note-{id}.txt` | `kb-docs/dev/{filename}` | ✅ same prefix |
| Corporate | `kb-docs/fortress/note-{id}.txt` | `kb-docs/fortress/{filename}` | ✅ dead code (blocked at CreateEntryAsync) |

**Metadata key alignment verified:**
- `ForgeService` writes `{ "ownerId": userId }` for Personal/Developer → `KnowledgeBaseService` filters `Key = "ownerId"` ✅
- `ForgeService` writes `{ "teamId": teamId }` for Team → `KnowledgeBaseService` filters `Key = "teamId"` ✅
- Developer KB uses no metadata filter (structural isolation) — `ownerId` attribute written is harmless ✅

**Bucket name:** `"fortress-tools"` — identical in `KbDocumentService` (class const L21), `ForgeService.UploadNoteToS3Async` (local const), `ForgeService.DeleteNoteFromS3Async` (local const) ✅

---

## Critical Issues — 0

None found.

---

## Important Issues — 0

None found.

---

## Nitpicks — 4

| # | File | Location | Issue |
|---|------|----------|-------|
| N1 | `ForgeService.cs` | `UploadNoteToS3Async` L38, `DeleteNoteFromS3Async` L73 | `BucketName` declared as local const in two methods. Extract to class-level `private const string BucketName = "fortress-tools"` to match `KbDocumentService` pattern and reduce drift risk. |
| N2 | `ForgeService.cs` | `GetNoteS3Key()` L29, `UploadNoteToS3Async` L54 | Corporate S3 branches are unreachable (Corporate create is blocked, Corporate write access denied). Not a risk, just dead code. |
| N3 | `ForgeService.cs` | `UploadNoteToS3Async` L54 | `entry.TeamId!.Value.ToString()` uses null-forgiving operator. A null `TeamId` on a Team-tier entry would throw a `NullReferenceException` silently swallowed by the outer catch. A guard (`entry.TeamId ?? throw new InvalidOperationException(...)`) would make the failure visible in logs. |
| N4 | `ForgeService.cs` | CreateEntryAsync L218, UpdateEntryAsync L258, DeleteEntryAsync L291 | Ingest tier switch is copy-pasted three times. Extract to `private static KbTier GetIngestTier(KbTier tier)` helper. |

None of these block shipment.

---

## Key Findings

### Critical checks all green

**C1 — S3 key prefix:** Notes land in the same prefixes Bedrock KB data sources watch (`kb-docs/personal/{userId}/`, `kb-docs/teams/{teamId}/`, etc.). Confirmed by cross-referencing against `KbDocumentService` key patterns.

**C2 — Metadata format:** `{ metadataAttributes: { "ownerId": "..." } }` for personal, `{ metadataAttributes: { "teamId": "..." } }` for team. Exact match with `KbDocumentService`. `KnowledgeBaseService` filters on these exact key names. Notes will be properly scoped to the right users.

**C3 — Non-fatal S3 ops:** In Create and Update, `db.SaveChangesAsync()` is called *before* the try/catch S3 block — S3 failure cannot prevent the DB write. In Delete, DB remove runs *after* the try/catch — same result. Ingestion calls inside the try are protected by `StartIngestionAsync`'s own internal catch, so they cannot propagate.

**C4 — Ingestion triggered:** All three methods call `_kbDocumentService.StartIngestionAsync(ingestTier)` after the S3 write. ConflictException handling (already-in-progress) queues a retry via `KbSyncRetryService`. Notes will be retrievable after the next ingestion completes.

**C5 — Deterministic key on update:** `GetNoteS3Key()` is keyed solely on `entry.Id`. Update does not modify `Id`. PutObject will overwrite the existing S3 object with updated content. ✅

**DI — IAmazonS3 lifetime:** `AddSingleton<IAmazonS3>` in Program.cs L113. ✅ (Anti-pattern from MEMORY.md would have been `AddScoped` — not the case here.)

---

## Positive Observations

- Non-fatal pattern is correctly applied: the try/catch wraps the entire S3 block and logs a Warning with enough context to diagnose failures without blocking the user-facing operation.
- Metadata structure is identical to `KbDocumentService` — no divergence from the established pattern.
- Ingestion tier mapping is correct and complete for all four tiers.
- `GetNoteS3Key()` is static and pure — no hidden state, easy to test.

---

## What to fix (nitpicks only — none block)

Tony can address these in a follow-up or in-flight, not required before merge:
- **N1:** Pull `BucketName` to class level in `ForgeService`
- **N4:** Extract the repeated ingest tier switch to a private helper

N2 and N3 are informational — no action required.

---

_Hawkeye — code-reviewer — 2026-04-08_
