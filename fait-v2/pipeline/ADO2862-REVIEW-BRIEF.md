# REVIEW Brief: ADO#2862 — FAIT v2 FIRM→FAIT v2 manual push

**ADO WI:** #2862 (Fortress project)
**Review Cycle:** 1
**Build Commit:** `6472089`

---

## MANDATORY: Use Claude Code CLI

```bash
CLAUDE_CODE_ENTRYPOINT=ado-pipeline \
CLAUDE_CODE_DISABLE_AUTO_MEMORY=1 \
CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1 \
CLAUDE_CODE_GLOB_TIMEOUT_SECONDS=30 \
cat /home/fredw/projects/fip/fait-v2/pipeline/ADO2862-REVIEW-BRIEF.md | \
claude --model sonnet --print --dangerously-skip-permissions
```

Working directory: `/home/fredw/projects/fip/`

---

## What Changed

**FAIT v2 (fait-v2/):**
- `Data/Models/PushedMessage.cs` — new model for `pushed_messages` table
- `Data/FaitV2DbContext.cs` — `PushedMessages` DbSet + FK config
- EF migration `AddPushedMessages` (+ designer + snapshot)
- `Program.cs` — `POST /api/agent/push-message` endpoint

**FIRM (firm/):**
- `Components/Pages/MeetingDetail.razor` — "Send to FAIT v2 Assistant" button
- `appsettings.json` — `FaitV2:BaseUrl`

---

## Review Checklist

### FAIT v2 — API Endpoint
1. `POST /api/agent/push-message` requires authorization (`RequireAuthorization()` or `[Authorize]`) — unauthenticated calls must be rejected
2. Entra OID extracted from `User.FindFirst("oid")` or the full objectidentifier claim — not from a user-supplied body field
3. Graceful 400 (not 500) returned if user has no provisioned FAIT v2 account
4. `PushedMessage` model uses `string` for GUID fields (varchar(36), `GuidFormat=None` pattern)
5. EF migration does not use raw SQL — uses EF Core migration API (`CreateTable`, `AddColumn`, etc.)
6. No S3 references in this WI (Aurora-only)
7. No Cognito references

### FIRM — Send Button
8. Button visible only to meeting owner or admin role — not all users
9. Auth cookie forwarded on the HTTP call to FAIT v2 (FIP shared cookie covers both domains)
10. Success/error feedback shown inline to user (not silent)
11. Graceful error message if user has no FAIT v2 account (matches the 400 response)
12. `FaitV2:BaseUrl` read from config — not hardcoded
13. No data stored beyond existing FIRM meeting records

### Code Quality
14. No hardcoded colors/fonts/sizes in Razor (CSS variables only)
15. `dotnet build` 0 errors in both fait-v2 and firm (confirmed in build report)

---

## ADO Tracking (MANDATORY)

After review complete:
```bash
mcporter call devops.add_comment --args '{
  "project": "Fortress",
  "id": 2862,
  "text": "**[Hawkeye — REVIEW cycle 1]**\nCode review {PASS|NEEDS-CHANGES}. Cycles: 1. {summary}"
}'
```

---

## Deliverables

1. Review Report: `/home/fredw/projects/fip/fait-v2/pipeline/ADO2862-REVIEW-REPORT-C1.md`
2. Verdict: PASS / NEEDS-CHANGES / FAIL
3. If NEEDS-CHANGES: file + line + exact fix
