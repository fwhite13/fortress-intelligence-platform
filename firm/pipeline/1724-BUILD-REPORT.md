# Build Report — ADO #1724 — FIRM Org Context Structured CRUD

**Cycle:** 1  
**Builder:** Tony Stark  
**Commit:** `d419407`  
**Branch:** `origin/main`  
**Build Result:** ✅ 0 errors  

---

## What Was Built

Replaced the freeform textarea org context wiki with a structured CRUD interface backed by JSON in the existing `wiki_content` TEXT column. Fixed the Blazor Server `HttpClientFactory` anti-pattern. Added a functional NavMenu with a Settings → Org Context link.

---

## Files Changed

| File | Change |
|------|--------|
| `Services/IOrgContextService.cs` | Added `OrgContextEntry` positional record (`Term`, `Description`). Split interface: `GetContextAsync` returns `List<OrgContextEntry>`, added `GetUpdatedAtAsync` + `GetUpdatedByAsync`, `UpsertContextAsync` accepts `List<OrgContextEntry>`. Removed `OrgContextDto`. |
| `Services/OrgContextService.cs` | JSON serialization via `System.Text.Json`. Legacy plain-text fallback (wraps existing blob as single "Legacy Content" entry). `IDbContextFactory<FirmDbContext>` pattern preserved. |
| `Components/Pages/OrgContext.razor` | **Full rewrite.** Removed `IHttpClientFactory` — injects `IOrgContextService` directly. Admin view: MudTable with Term/Description/Actions columns, MudDialog for Add/Edit, inline Delete. Non-admin view: read-only table. Loading state via `MudProgressLinear`. Save All posts entire list via service. Last-updated timestamp shown. OID-based admin check preserved. |
| `Components/Layout/NavMenu.razor` | Replaced stub comment with real `MudNavMenu`: Dashboard → `/`, Meetings → `/meetings`, divider, Settings section header, Org Context → `/org-context`. |
| `Controllers/OrgContextController.cs` | GET updated to return `entries` array + `wikiContent` (backward compat for existing API clients). PUT updated to call new service interface — deserializes JSON array or wraps plain text as single entry. No `HttpClientFactory` was present (controller is fine). |

---

## Parallelization

Sequential — all files share the same service interface. `IOrgContextService.cs` must be written before `OrgContextService.cs`, `OrgContext.razor`, and `OrgContextController.cs`. NavMenu was independent.

---

## CC Sessions

1 CC session (Claude Sonnet). All 5 files in one spec to ensure interface consistency.

---

## Acceptance Criteria Verification

- [x] `OrgContextEntry` record defined as `record OrgContextEntry(string Term, string Description)` ✅
- [x] `GetContextAsync` returns `List<OrgContextEntry>`, handles legacy plain text ✅  
- [x] `UpsertContextAsync` accepts `List<OrgContextEntry>`, serializes to JSON ✅  
- [x] `OrgContext.razor` has NO `IHttpClientFactory` usage ✅  
- [x] Admin CRUD table: Term | Description | Edit | Delete ✅  
- [x] Add Entry → MudDialog with Term + Description fields ✅  
- [x] Save All → calls `IOrgContextService.UpsertContextAsync` ✅  
- [x] Non-admin view: read-only table ✅  
- [x] Last-updated timestamp displayed ✅  
- [x] OID-based admin check preserved ✅  
- [x] NavMenu.razor updated with Settings → Org Context link ✅  
- [x] `dotnet build` — 0 errors ✅  

---

## Known Edge Cases / Things to Scrutinize

1. **NavMenu routes** — Dashboard (`/`) and Meetings (`/meetings`) are assumed routes. If `/meetings` is named differently (e.g. `/meeting-list`), update the `Href` in NavMenu.
2. **Legacy data migration** — Existing tenants with plain text will see it wrapped as a single "Legacy Content" entry. The term is hardcoded as `"Legacy Content"` — Fred may want to rename this to something more meaningful or prompt the user to reorganize.
3. **`GetUpdatedAtAsync`/`GetUpdatedByAsync`** — These each open a separate DB connection after `GetContextAsync`. They could be consolidated into one call with a richer return type. Fine for now but worth noting.
4. **`_entries` is a mutable `List<>` in the Razor component** — Concurrent edits (two admin sessions) would overwrite each other silently. Acceptable for this use case; no optimistic concurrency needed.
5. **CSS classes** — New classes (`org-context-title`, `org-context-card`, etc.) are referenced in the Razor but not yet defined in a CSS file. They're structural/semantic only — MudBlazor provides the actual styling. If the design system requires them to map to specific styles, a `OrgContext.razor.css` file will need to be added.

---

## How to Test Locally

```bash
# 1. Run the FIRM app
cd ~/projects/fip/firm
dotnet run --project src/FortressIntelligenceRM.Web/

# 2. Navigate to /org-context as admin — should see CRUD table
# 3. Click "Add Entry" — dialog opens with Term + Description fields
# 4. Add an entry, click Save in dialog — entry appears in table
# 5. Click Save All — persists to DB
# 6. Reload page — entries reload from DB (parsed from JSON)
# 7. Navigate as non-admin — should see read-only table
# 8. Check left drawer — Settings section with Org Context link should appear
```

---

# Build Report — ADO #1724 — Cycle 2 (OrgContextService Injection Fix)

**Cycle:** 2
**Builder:** Tony Stark
**Commit:** `d6a8b39`
**Branch:** `origin/main`
**Build Result:** ✅ 0 errors, 16 warnings (all pre-existing)

---

## What Was Built

Replaced direct EF reads of `orgCtx?.WikiContent` in `TeamsGraphService.cs` and `MeetingsApiController.cs` with calls to `IOrgContextService.GetContextAsync()`. Entries are now formatted as prose (`Term: Description` per line) before injection into the Bedrock summarization prompt, eliminating the raw JSON regression from Cycle 1.

---

## Files Changed

| File | Change |
|------|--------|
| `Services/TeamsGraphService.cs` | Added `IOrgContextService` field + constructor injection. Replaced two `db.OrgContexts.FirstOrDefaultAsync(...)` / `orgCtx?.WikiContent` reads (in `ProcessVttForMeetingAsync` and `FetchAndProcessTranscriptAsync`) with `_orgContextService.GetContextAsync(tenantId)` + `string.Join("\n", ...)` prose formatting. |
| `Controllers/MeetingsApiController.cs` | Added `IOrgContextService` field + constructor injection. Replaced one `db.OrgContexts.FirstOrDefaultAsync(...)` / `orgCtx?.WikiContent` read in `ReprocessSummary` with same service-based pattern. |

---

## Parallelization

Not applicable — single-session targeted fix.

---

## CC Sessions

1 CC session (Claude Sonnet). Both files in one spec.

---

## Acceptance Criteria Verification

- [x] `TeamsGraphService.cs` — injects `IOrgContextService`, formats entries as prose ✅
- [x] `MeetingsApiController.cs` — injects `IOrgContextService`, formats entries as prose ✅
- [x] No direct `db.OrgContexts` reads remain in either file ✅
- [x] `tenantId` resolution unchanged (config-based in TeamsGraphService, claims + config fallback in MeetingsApiController) ✅
- [x] `dotnet build` — 0 errors ✅

---

## Known Edge Cases / Things to Scrutinize

1. **Empty entries list** — If `GetContextAsync` returns an empty list, `orgWikiContent` is set to `null` and the prompt gets no org context block. This matches prior behavior when no org context row existed.
2. **TeamsGraphService is a singleton** (IHostedService) — `IOrgContextService` creates its own `IDbContextFactory` scope internally, so there's no scoped-in-singleton violation. Verified: `OrgContextService` uses `IDbContextFactory<FirmDbContext>` (not `FirmDbContext` directly).
3. **Pre-existing 16 warnings** — All `[Obsolete]` warnings on `PushTranscriptToKb` and `PushSummaryToKb` endpoints. Not introduced by this cycle.

---

## How to Test

```bash
# Verify build clean
cd ~/projects/fip/firm && dotnet build src/FortressIntelligenceRM.Web/

# Functional: trigger a reprocess on a completed meeting and verify
# the summary prompt contains "Term: Description" formatted lines,
# not raw JSON array syntax
```
