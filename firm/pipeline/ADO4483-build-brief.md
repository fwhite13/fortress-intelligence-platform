# Build Brief: ADO#4483 — FIRM: Restore Mind Map Tab

## Context
This is a **verification-only** task. Code has already been merged into main via commit `f05d8268` (2026-05-27). No new code changes are expected. Your job is to verify the commit is clean, all files are present, the implementation is complete, and the build compiles.

**Repo:** `/home/fredw/projects/fip/`
**FIRM project directory:** `/home/fredw/projects/fip/firm/`
**FIRM solution file:** find it under `firm/src/`

---

## Step 1: Verify commit f05d8268 is on origin/main

Run:
```bash
cd /home/fredw/projects/fip && git log --oneline origin/main | head -20
```
Confirm `f05d8268` is present. Also run:
```bash
cd /home/fredw/projects/fip && git show --name-only f05d8268 | head -40
```
List all files changed in that commit.

---

## Step 2: Verify all 12 expected files are present

Check that each of these files exists (use `ls` or similar):

1. `firm/src/FIRM.Web/Components/Pages/MeetingDetail.razor`
2. `firm/src/FIRM.Web/Services/MindmapService.cs` (NEW file)
3. `firm/src/FIRM.Web/Models/FirmMeetingMindmap.cs` (NEW file)
4. `firm/src/FIRM.Web/Controllers/MeetingsApiController.cs`
5. `firm/src/FIRM.Infrastructure/Data/DatabaseInitializationService.cs`
6. `firm/src/FIRM.Infrastructure/Data/FirmDbContext.cs`
7. `firm/src/FIRM.Infrastructure/Models/FirmMeeting.cs`
8. `firm/src/FIRM.Infrastructure/Models/FirmUser.cs`
9. `firm/src/FIRM.Web/Program.cs`
10. `firm/src/FIRM.Infrastructure/Services/S3Service.cs`
11. `firm/appsettings.json`
12. `firm/src/FIRM.Web/wwwroot/js/firm-utils.js`

---

## Step 3: Read each file and check for completeness

For each file, read it and verify:
- No `TODO` or `throw new NotImplementedException()` stubs
- No empty method bodies where implementation is expected
- No placeholder comments like "// implement this"

Pay special attention to:
- `MindmapService.cs` — should have real Bedrock generation code, real DB storage code, real S3 storage
- `FirmMeetingMindmap.cs` — should be a complete model with all needed properties
- `MeetingDetail.razor` — should have Mind Map tab UI, `OnMindMapTabSelected`, `LoadMindmapAsync`, `RegenerateMindmap`, and `@inject IMindmapService`

---

## Step 4: DB Init Rules Check — DatabaseInitializationService.cs

Read `firm/src/FIRM.Infrastructure/Data/DatabaseInitializationService.cs` carefully.

### Rule 4a: firm_meeting_mindmaps table creation
- Find the CREATE TABLE statement for `firm_meeting_mindmaps`
- Confirm it is present

### Rule 4b: alterStatements coverage
- Find the `alterStatements` list/array in the file
- Verify that every column in the `firm_meeting_mindmaps` table that was added via EF migration is ALSO represented in `alterStatements`
- `alterStatements` is the idempotent migration path — both the CREATE and ALTER must cover the same columns

### Rule 4c: CHAR(36) collation
- Any `CHAR(36)` column in this table MUST include `CHARACTER SET ascii COLLATE ascii_general_ci`
- Check all CHAR(36) columns in both CREATE TABLE and alterStatements

### Rule 4d: alterStatements error handling
- The `alterStatements` execution loop MUST log-and-continue on exceptions (never abort the whole migration)
- Verify try/catch pattern wraps individual alter executions

### Rule 4e: No IF NOT EXISTS syntax
- Aurora MySQL 8.0 does NOT support `ADD COLUMN IF NOT EXISTS`
- Search for this syntax in the file — it must NOT appear

---

## Step 5: MindmapService.cs checks

Read `firm/src/FIRM.Web/Services/MindmapService.cs` and check:

### Rule 5a: Bedrock InvokeModelAsync pattern
- The service should call Bedrock using `InvokeModelAsync` (or `InvokeModelWithResponseStreamAsync`)
- Look at other FIRM services that call Bedrock to confirm the pattern matches
- Check: `firm/src/FIRM.Web/Services/` for other services using Bedrock

### Rule 5b: MySqlConnectionStringBuilder GuidFormat
- If `MindmapService.cs` creates a `MySqlConnectionStringBuilder`, it MUST include `GuidFormat = MySqlGuidFormat.None`
- Search for any MySqlConnection or connection string construction in this file

---

## Step 6: MeetingsApiController.cs checks

Read `firm/src/FIRM.Web/Controllers/MeetingsApiController.cs` and verify:
- The `/mindmap` endpoint exists and is properly attributed (e.g., `[HttpGet]` or `[HttpPost]`)
- The `/mindmap/export` endpoint exists
- Mobile endpoints for mindmap exist
- All new endpoints have `[Authorize]` or equivalent authorization
- No obviously missing route attributes

---

## Step 7: Run dotnet build

Find the FIRM solution file:
```bash
find /home/fredw/projects/fip/firm -name "*.sln" | head -5
```

Then run:
```bash
cd /home/fredw/projects/fip && dotnet build firm/src/FIRM.sln --configuration Release 2>&1
```
(Adjust solution file path if needed)

Capture the full output. Report:
- Exit code (0 = success, non-zero = failure)
- Any errors (lines containing "error")
- Any warnings that look like they could be problems

---

## Step 8: Acceptance Criteria Verification

Based on what you read in the code, mark each AC:
- **AC1:** Mind Map tab is present in `MeetingDetail.razor` and only shown for Complete meetings
- **AC2:** "Generate Mind Map" button triggers Bedrock generation via `MindmapService`
- **AC3:** mind-elixir JS library renders the map (check `firm-utils.js` for `firmMindmap.render`)
- **AC4:** Regenerate and Export .mm functionality exists
- **AC5:** `firm_meeting_mindmaps` table is auto-created by `DatabaseInitializationService`

---

## Output Instructions

Write the Build Report to:
`/home/fredw/projects/fip/firm/pipeline/ADO4483-BUILD-REPORT.md`

Use this exact format:

```markdown
# Build Report: ADO#4483

## Summary
[1-2 sentence summary of findings]

## CC Invocation
[Notes on this CC run]

## Commit Verification (f05d8268 on main)
[PASS/FAIL — commit hash and message]

## Files Present (12/12)
[List each file with PRESENT/MISSING]

## File Completeness Check
[Summary — any TODOs, stubs, or incomplete implementations found]

## DB Init Rules Check
- Rule 4a (firm_meeting_mindmaps CREATE TABLE): [PASS/FAIL]
- Rule 4b (alterStatements coverage): [PASS/FAIL — list which columns covered]
- Rule 4c (CHAR(36) collation): [PASS/FAIL]
- Rule 4d (alterStatements error handling — log-and-continue): [PASS/FAIL]
- Rule 4e (No IF NOT EXISTS syntax): [PASS/FAIL]

## MindmapService Checks
- Rule 5a (Bedrock InvokeModelAsync pattern): [PASS/FAIL]
- Rule 5b (GuidFormat = MySqlGuidFormat.None if applicable): [PASS/N/A/FAIL]

## API Controller Checks
[/mindmap endpoint: PASS/FAIL, /mindmap/export: PASS/FAIL, authorization: PASS/FAIL]

## Build Result (dotnet build exit code)
[PASS (exit 0) or FAIL (exit N)]
[Error/warning summary if any]

## Self-Review Checklist
- [ ] AC1: Mind Map tab present in MeetingDetail for Complete meetings
- [ ] AC2: Generate Mind Map triggers Bedrock generation
- [ ] AC3: mind-elixir renders map
- [ ] AC4: Regenerate + Export .mm work
- [ ] AC5: firm_meeting_mindmaps table auto-created
- [ ] DB INIT RULES: alterStatements, CHAR(36) collation, no IF NOT EXISTS
- [ ] GuidFormat = MySqlGuidFormat.None (if applicable)

## Issues Found
[List any issues, or "None"]

## ADO Comment
[Text ready to post to ADO#4483]
```

After writing the report file, print its full contents to stdout.

---

## Done

That's all. No code changes. No commits. Read, verify, build, report.
