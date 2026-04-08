# Build Report — WI #1667: KB Notes Not Retrievable

**Date:** 2026-04-08  
**Engineer:** Tony Stark (software-engineer)  
**Commit:** `163f4c3df6bbfe0f47b663ab18b7d28f0dd2b25c`  
**Build:** ✅ 0 errors, 31 pre-existing warnings

---

## Investigation Findings

### Step 1 — How notes vs documents are stored

**Documents:**
- Written to S3 via `KbDocumentService.UploadDocumentAsync()`
- S3 key pattern: `kb-docs/personal/{userId}/{filename}`, `kb-docs/teams/{teamId}/{filename}`, `kb-docs/fortress/{filename}`, `kb-docs/dev/{filename}`
- Companion `.metadata.json` written alongside each document
- Tracked in `project_documents` DB table with ingestion status
- After upload: `StartIngestionAsync()` is called → Bedrock ingestion job triggered

**Notes (`KbEntry`):**
- Written **only to MySQL** via `ForgeService.CreateEntryAsync()`
- Stored in `kb_entries` table (fields: Id, UserId, TeamId, Tier, Title, Content, Tags, SourceUrl, timestamps)
- **Never written to S3**
- **Never trigger a Bedrock ingestion job**

### Step 2 — Ingestion pipeline

- `KbDocumentService.StartIngestionAsync(tier)` calls `bedrockAgent.StartIngestionJobAsync()` with the tier's KB ID and Data Source ID
- Documents: ✅ ingestion triggered after every upload and delete
- Notes: ❌ no ingestion ever triggered — not even the S3 write occurs

### Step 3 — Post-ingestion presence

Not applicable — notes never reached S3 at all.

### Step 4 — Retrieval filter

`ForgeQueryService.GetKbContextAsync()` / `GetKbContextMultiQueryAsync()` calls Bedrock Retrieve API with `ownerId` or `teamId` metadata filters. No filter is excluding notes — the problem is upstream: notes are never in the vector store to filter.

---

## Root Cause

**`ForgeService` had no S3 or Bedrock dependencies.** When a user creates a note, `ForgeService.CreateEntryAsync()` writes to MySQL and returns — end of story. The Bedrock KB data source only reads from S3. Since notes never touched S3, they were never ingested and are permanently invisible to AI retrieval.

Documents work because `KbDocumentService.UploadDocumentAsync()` explicitly puts the file in S3 under the correct prefix and calls `StartIngestionAsync()`.

---

## Fix

**Modified file:** `src/FortressAI.Web/Services/ForgeService.cs`

### What changed

1. **Added `IAmazonS3` and `KbDocumentService` constructor injection** — ForgeService now has the tools to write to S3 and trigger ingestion

2. **Added `GetNoteS3Key(KbEntry entry)`** — generates S3 key matching the same prefix structure as documents:
   - Personal: `kb-docs/personal/{userId}/note-{entryId}.txt`
   - Team: `kb-docs/teams/{teamId}/note-{entryId}.txt`
   - Corporate: `kb-docs/fortress/note-{entryId}.txt`
   - Developer: `kb-docs/dev/note-{entryId}.txt`

3. **Added `UploadNoteToS3Async(KbEntry entry)`** — writes note as plain text to S3 with companion `.metadata.json` (same metadata pattern as documents: `ownerId` for personal, `teamId` for team)

4. **Added `DeleteNoteFromS3Async(KbEntry entry)`** — removes note `.txt` and `.metadata.json` from S3

5. **`CreateEntryAsync`** — after DB insert: calls `UploadNoteToS3Async` + `StartIngestionAsync(tier)` in a try/catch (non-fatal — note is in DB, S3 sync failure is logged as Warning)

6. **`UpdateEntryAsync`** — after DB update: calls `UploadNoteToS3Async` + `StartIngestionAsync(tier)` (idempotent S3 overwrite ensures updated content is re-ingested)

7. **`DeleteEntryAsync`** — before DB remove: calls `DeleteNoteFromS3Async` + `StartIngestionAsync(tier)` so Bedrock re-ingests and the deleted note drops out of the vector store

### Error handling

All S3/ingestion operations are wrapped in try/catch. Failures are logged at Warning level with `[ForgeService]` prefix. The DB write is always the primary operation — S3 is secondary and never blocks the user.

---

## Build Result

```
dotnet build src/FortressAI.Web/FortressAI.Web.csproj
  0 Error(s)
  31 Warning(s) — all pre-existing MUD0002 analyzer warnings, unrelated to this change
```

---

## Files Modified

| File | Change |
|------|--------|
| `src/FortressAI.Web/Services/ForgeService.cs` | Added S3 sync + ingestion trigger for note create/update/delete |

---

## How to Test

1. Deploy to staging
2. Create a new KB note via the Knowledge Base UI
3. Wait 1–5 minutes for Bedrock ingestion to complete
4. Chat with AI referencing the note's content
5. Verify the AI surfaces the note in its response
6. Edit the note, verify updated content appears after re-ingestion
7. Delete the note, verify content no longer appears after re-ingestion

---

## Things for Clint to Scrutinize

1. **Existing notes** — notes created before this fix are NOT in S3. A one-time backfill of existing `kb_entries` rows to S3 would be needed for those to become retrievable. This is out of scope for this WI but should be a follow-up task.

2. **Corporate tier** — `CreateEntryAsync` blocks Corporate creates with a guard (`throw InvalidOperationException`), so the S3 sync in `CreateEntryAsync` never executes for Corp. However `UpdateEntryAsync` and `DeleteEntryAsync` do allow Corp writes — the S3 sync there will work when admin Corp editing is eventually enabled.

3. **S3 key collision** — if a user had a file named `note-{id}.txt` uploaded manually, there could be a key collision. This is extremely unlikely but worth noting. The `note-` prefix makes it unambiguous that these are ForgeService-managed objects.
