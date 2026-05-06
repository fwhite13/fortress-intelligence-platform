# Build Report — ADO#2834

## KB File Enumeration Fix: S3-authoritative listing + preserve filenames with extensions

**Build result:** ✅ SUCCEEDED  
**Commit:** `8ea37fc3778015aa883f48b8b93803e914746a91`  
**Date:** 2026-05-06  
**Agent:** Tony Stark (BUILD)

---

## What was built

Two changes across two repos in the FIP monorepo:

1. **FAIT 1-liner** — `KnowledgeBaseService.cs` line 323: removed `System.IO.Path.GetFileNameWithoutExtension()` wrapper so KB context headers now show full filenames with extensions (e.g. `report.pdf` not `report`).

2. **fip-mcp new tool** — `list_kb_files.js` — S3 `ListObjectsV2` enumeration tool registered in `server.js`. Lists files in a user's KB by prefix, excludes sidecar files, returns `{filename, size_bytes, last_modified}`.

---

## Files changed

| File | Change |
|------|--------|
| `fait/src/FortressAI.Web/Services/KnowledgeBaseService.cs` | Line 323: removed `GetFileNameWithoutExtension()` wrapper |
| `services/fip-mcp/package.json` | Added `@aws-sdk/client-s3: ^3.0.0` to dependencies |
| `services/fip-mcp/src/tools/list_kb_files.js` | New file — 80 lines |
| `services/fip-mcp/src/server.js` | Import + registration of `list_kb_files` tool |

---

## User ID field used in list_kb_files.js

**`user.user_id`** — sourced from `auth.js` JWT middleware (`payload.oid`, Entra OID). This is the FAIT userId guid used for personal KB S3 prefix: `kb-docs/personal/{user.user_id}/`. Consistent with how `search_kb.js` scopes personal KB queries.

---

## Parallelization

Not applicable — tasks were sequential by design:
1. Read plan + source files
2. Write CC brief
3. CC execution (single run covering both repos atomically)

---

## CC sessions run

1 session — single CC Sonnet run covering both repos.

---

## Acceptance criteria

- [x] `KnowledgeBaseService.cs` line 323: `GetFileNameWithoutExtension` wrapper removed — full filename preserved
- [x] `list_kb_files.js` exists with S3 `ListObjectsV2`, entitlement check, personal/team/corp prefix routing, `.metadata.json` + `-bda-text.txt` filtering
- [x] `@aws-sdk/client-s3` added to `package.json`
- [x] Tool imported and registered in `server.js`
- [x] No other files touched (beyond ADO pipeline files written by CC)

---

## ECS Task Def — Action Required for Rhodey

`KB_BUCKET` env var must be added to the `fip-mcp` ECS task definition before deploy:  
**Key:** `KB_BUCKET`  
**Value:** `fortress-tools`

Tool defaults to `fortress-tools` if env var is absent, but explicit task def config is preferred.

---

## Known edge cases / things Clint should scrutinize

1. **`s3_key` field in response** — `list_kb_files` returns `s3_key` (the full S3 object key) in addition to `filename`. This wasn't explicitly required by the spec but is useful for disambiguation. Can be removed if Clint considers it over-exposure.

2. **Corp KB prefix** — `kb-docs/fortress/` is hardcoded for `KB_TYPE.CORP`. If the actual bucket layout differs, this will return empty results (not an error). Worth a quick verify with Rhodey.

3. **CC also committed the ADO2834-PLAN.md and ADO2833 pipeline files** — Those were unintentionally staged. No functional impact — pipeline docs only.

---

## How to test locally

```bash
# fip-mcp — start dev server with required env vars
cd /home/fredw/projects/fip/services/fip-mcp
npm install  # picks up new @aws-sdk/client-s3
KB_BUCKET=fortress-tools BEDROCK_REGION=us-east-1 npm run dev

# Then call via MCP client with a valid JWT:
# list_kb_files({ kb_id: "<personal_kb_id>" })
# Expected: { kb_id, kb_type, prefix, file_count, files: [{filename, size_bytes, last_modified, s3_key}] }

# FAIT — verify KB context headers show full extensions
# Search KB with a document that has a known extension (e.g. report.pdf)
# Confirm source header reads "report.pdf" not "report"
```
