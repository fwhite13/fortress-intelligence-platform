## Build Report — ADO #1714: Org Context Wiki

### What was built
Per-org wiki feature for FIRM: a DB-backed text field per tenant injected into Bedrock summarization prompts to improve AI accuracy for org-specific names, roles, and terminology.

### Files Created
- `Models/FirmOrgContext.cs` — EF model for firm_org_context table
- `Services/IOrgContextService.cs` — Interface + OrgContextDto
- `Services/OrgContextService.cs` — Implementation (GetContext, UpsertContext)
- `Controllers/OrgContextController.cs` — GET/PUT /api/org-context
- `Components/Pages/OrgContext.razor` — Admin UI at /org-context

### Files Modified
- `Data/FirmDbContext.cs` — Added OrgContexts DbSet + entity config
- `Data/DatabaseInitializationService.cs` — Added CREATE TABLE for firm_org_context
- `Services/TeamsGraphService.cs` — SummarizeAsync signature + org context injection in ProcessVttForMeetingAsync and FetchAndProcessTranscriptAsync
- `Controllers/MeetingsApiController.cs` — Org context injection in ReprocessSummary
- `Program.cs` — IOrgContextService DI registration

### Design Decisions
1. **Tenant ID resolution**: `tid` claim first, fall back to `Firm:GraphTenantId` config. FIRM is single-tenant so config fallback is reliable.
2. **Admin gate**: `Firm:AdminEntraOid` config value matched against `oid` claim. Falls back to `roles` claim check. Same pattern as other FIP apps.
3. **Prompt position**: Org context block prepended before the main persona instruction. This is the "system context" injection pattern — model sees org context first.
4. **Error handling**: All org context operations are non-fatal. If DB call fails, summarization continues without context (logged as Warning).
5. **UI placement**: New dedicated page `/org-context` rather than embedding in MeetingDetail — cleaner separation, not per-meeting config.

### Build Result
PASS — 0 errors, 12 warnings (all pre-existing warnings, none introduced by this change)

### Commit Hash
0c429e1b2178d6ff965be3c6c484999f22a6ce82

### Known Edge Cases / Things Clint Should Scrutinize
- Tenant ID from `tid` claim: in FIRM's cookie-based auth via FIP, the `tid` claim may not be present. Config fallback (`Firm:GraphTenantId`) is the primary path.
- The `IsAdmin` check on the Blazor page duplicates controller logic — both use `Firm:AdminEntraOid`. DRY improvement could extract this to a shared service in a future PR.
- OrgContext Blazor page calls `/api/org-context` via the `local` named HTTP client (same-container call). This is consistent with other pages.
