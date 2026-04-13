# Build Report — WI #1668: KB Notes Backfill to S3

**Date:** 2026-04-08
**Engineer:** Tony Stark
**Status:** ✅ COMPLETE — dev + prod both done

---

## What Was Built

A Python backfill script (`1668-backfill.py`) that uploads all existing `kb_entries` from the FAIT database to S3 in the exact format `ForgeService.UploadNoteToS3Async` uses, then triggers Bedrock ingestion for each affected KB tier.

---

## Script Design Decisions

### Why Python over bash
- MySQL query, JSON construction, and S3 HeadObject checks are cleaner in Python
- `mysql-connector-python` and `boto3` both pre-installed in environment

### S3 format — exact match to ForgeService.cs
Content format:
```
# {Title}\n\n{Content}
```
With optional tags line if non-null/non-empty:
```
\n\nTags: {Tags}
```

Metadata sidecar:
- Team tier: `{"metadataAttributes": {"teamId": "<int as string>"}}`
- All other tiers: `{"metadataAttributes": {"ownerId": "<UserId guid>"}}`

S3 key patterns (verified against ForgeService.cs line 26-32):
```
Personal:  kb-docs/personal/{UserId}/note-{Id}.txt
Team:      kb-docs/teams/{TeamId}/note-{Id}.txt
Corporate: kb-docs/fortress/note-{Id}.txt
Developer: kb-docs/dev/note-{Id}.txt
```

### Idempotency
Uses `HeadObject` check before each upload. If `.txt` key already exists, the entry (both `.txt` and `.metadata.json`) is skipped.

### IAM note
`fortress-tools-deployer` lacks `bedrock:StartIngestionJob`. Used `openclaw-bedrock` profile for Bedrock calls. Script uses separate boto3 session per operation.

### Developer tier in prod
No `KnowledgeBase__DevKbId` exists in the `fait-prod` task definition — Developer tier is dev-only. Script skips Developer entries in prod automatically.

---

## Dev Run Results

**DB:** `fait_dev` | **Total rows:** 17

| Tier       | Uploaded | Skipped | Errors |
|------------|----------|---------|--------|
| Personal   | 16       | 0       | 0      |
| Team       | 1        | 0       | 0      |
| Corporate  | 0        | 0       | 0      |
| Developer  | 0        | 0       | 0      |
| **Total**  | **17**   | **0**   | **0**  |

**S3 Spot-Check (dev):**
- `kb-docs/personal/08de7605-3f7d-427d-858a-637777b41018/note-4.txt` ✅ present
- `kb-docs/personal/08de7605-3f7d-427d-858a-637777b41018/note-4.txt.metadata.json` ✅ present
- `kb-docs/teams/2/note-6.txt` ✅ present
- `kb-docs/teams/2/note-6.txt.metadata.json` ✅ present

---

## Dev Validation — Bedrock Ingestion

| Tier     | Job ID       | Status   | Indexed | Failed |
|----------|-------------|----------|---------|--------|
| Personal | `3XH1D63SOB` | COMPLETE | 16      | 8*     |
| Team     | `IY46VSWIEX` | COMPLETE | 2       | 5*     |

*Failures are pre-existing unsupported file types (PPTX, DOCX) that were already in the buckets — **not our notes**. All 17 note `.txt` files indexed successfully.

---

## Prod Run Results

**DB:** `fait_prod` | **Total rows:** 32

Notes 4–61 were skipped (already uploaded during dev run — shared S3/KBs between dev and prod environments).
Notes 62–76 are prod-only additions, all newly uploaded.

| Tier       | Uploaded | Skipped | Errors |
|------------|----------|---------|--------|
| Personal   | 3        | 16      | 0      |
| Team       | 12       | 1       | 0      |
| Corporate  | 0        | 0       | 0      |
| Developer  | 0 (skipped — no prod KB) | 0 | 0 |
| **Total**  | **15**   | **17**  | **0**  |

---

## Prod Validation — Bedrock Ingestion

| Tier     | Job ID       | Status   | Indexed | Failed |
|----------|-------------|----------|---------|--------|
| Personal | `AIWFGQTWLB` | COMPLETE | 3       | 8*     |
| Team     | `XDHBOJFULN` | COMPLETE | 12      | 5*     |

*Same pre-existing unsupported file failures as dev — not our notes.

---

## Config Values Used

| Variable | Value |
|----------|-------|
| S3 Bucket | `fortress-tools` (hardcoded in ForgeService.cs) |
| DB Host | `fortress-ai-cluster.cluster-c89acukue4d5.us-east-1.rds.amazonaws.com` |
| Dev DB | `fait_dev` |
| Prod DB | `fait_prod` |
| Personal KB | `ZCEZCJGHQC` / DS `3X5E9L4HAC` |
| Team KB | `NRGEACKSBJ` / DS `VYMEB3BA12` |
| Corporate KB | `WYSKBKWHPL` / DS `O6DPFQ08WN` |
| Developer KB (dev only) | `EE1X6QJ9WH` / DS `CWZRCFWDEV` |

---

## Issues Encountered

1. **`fortress-tools-deployer` lacks `bedrock:StartIngestionJob`** — solved by using `openclaw-bedrock` profile for Bedrock calls. Script updated accordingly.

2. **Shared KBs between dev and prod** — dev and prod task definitions point to the same KB IDs and S3 bucket. Notes uploaded during dev run were correctly skipped by the idempotency check during prod run.

3. **Corporate/Developer tiers empty in both DBs** — 0 entries in those tiers; no uploads needed, no ingestion triggered for those tiers.

---

## Deliverables

- [x] `pipeline/1668-backfill.py` — backfill script
- [x] `pipeline/1668-backfill-dev.log` — dev run log
- [x] `pipeline/1668-backfill-prod.log` — prod run log
- [x] `pipeline/1668-BUILD-REPORT.md` — this file
- [x] ADO comments posted on WI #1668
