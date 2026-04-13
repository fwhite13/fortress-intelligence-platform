# Review Report — FIRM ADO #1724

**Verdict: NEEDS-CHANGES**
**Cycle:** 1
**Reviewer:** Hawkeye (code-reviewer)
**Commit:** d419407
**Date:** 2026-04-13

---

## CC Review Summary

CC ran full adversarial review across all 5 changed files plus downstream consumers (`TeamsGraphService.cs`, `MeetingsApiController.cs`). All 5 critical checks cleared. One confirmed blocking important issue identified: downstream consumers reading raw `wiki_content` from EF will receive JSON in the Bedrock prompt instead of human-readable text. Two additional important findings (redundant DB calls, term name inconsistency). Several nitpicks.

---

## Spec Compliance Check

**§ Codebase Map:**
- `IOrgContextService.cs` — ✅ modified as specified
- `OrgContextService.cs` — ✅ modified as specified
- `OrgContext.razor` — ✅ modified as specified
- `NavMenu.razor` — ✅ modified as specified
- `OrgContextController.cs` — ✅ modified as specified

**§ Out of Scope:**
- ✅ No out-of-scope changes detected. Only pipeline report files added alongside source changes.

**§ Acceptance Criteria:**
- [x] No `HttpClientFactory` in `OrgContext.razor` — ✅ zero references
- [x] `UpsertContextAsync` SQL parameterized — ✅ `{1}` positional, not interpolated
- [x] Legacy plain-text try/catch correct — ✅ fall-through verified
- [x] Admin check server-side — ✅ `AuthStateProvider` claims, not client-supplied
- [x] Controller `[Authorize]` + `IsAdmin()` intact — ✅ verified
- [x] NavMenu `/meetings` route exists — ✅ `Meetings.razor` has `@page "/meetings"`
- [ ] **Downstream consumers updated for JSON format change** — ❌ NOT met

**Spec compliance verdict:** ❌ NON-COMPLIANT (downstream consumers not updated)

---

## Consistency Audit

**Files Cross-Referenced:**
- `IOrgContextService.cs` ↔ `OrgContextService.cs` — ✅ interface matches implementation
- `IOrgContextService.cs` ↔ `OrgContext.razor` — ✅ correct injection and method calls
- `IOrgContextService.cs` ↔ `OrgContextController.cs` — ✅ correct usage
- `OrgContextService.cs:46` ↔ `OrgContextController.cs:71,76` — ❌ legacy wrap term mismatch ("Legacy Content" vs "Content")
- `OrgContextService.cs` (writes JSON) ↔ `TeamsGraphService.cs:216,366` (reads raw WikiContent) — ❌ **format mismatch, breaking downstream**
- `OrgContextService.cs` (writes JSON) ↔ `MeetingsApiController.cs:816` (reads raw WikiContent) — ❌ **same issue**

---

## Critical Issues — 0

All 5 critical checks passed.

| Severity | File | Issue | Status |
|----------|------|-------|--------|
| C1 | `OrgContext.razor` | No `HttpClientFactory`/`HttpClient` | ✅ PASS |
| C2 | `OrgContextService.cs` | SQL parameterization | ✅ PASS |
| C3 | `OrgContextService.cs` | Legacy try/catch correctness | ✅ PASS |
| C4 | `OrgContext.razor` | Admin check server-side | ✅ PASS |
| C5 | `OrgContextController.cs` | `[Authorize]` + `IsAdmin()` intact | ✅ PASS |

---

## Important Issues — 2

### I1: CONFIRMED BREAKING CHANGE — Downstream consumers receive raw JSON in Bedrock prompt

**Files:** `Services/TeamsGraphService.cs` (lines 216, 366, 492) | `Controllers/MeetingsApiController.cs` (line ~816)
**Category:** Correctness — downstream format break
**Issue:** Both `TeamsGraphService` and `MeetingsApiController` bypass `IOrgContextService` entirely. They read `orgCtx?.WikiContent` directly from the EF entity (`db.OrgContexts.FirstOrDefaultAsync`) and pass the raw string to `SummarizeAsync`. After this change, `wiki_content` stores JSON like:
```json
[{"term":"Fred White","description":"AI Lead"},{"term":"FIRM","description":"Meeting recording platform"}]
```
That raw JSON string gets injected verbatim into the Bedrock prompt:
```
[Org Context — use to improve accuracy of names, roles, and terminology]
[{"term":"Fred White","description":"AI Lead"}]
[End Org Context]
```
The org context block was designed for natural language. The LLM will likely still parse this, but it degrades prompt quality and is inconsistent with the prompt's stated intent.

**Impact:** Every meeting summarization and reprocess call produces lower-quality org context injection. Confirmed regression on the summarization pipeline.

**Fix:** Update both callers to use `IOrgContextService.GetContextAsync()` and format entries as human-readable text before injection. Add a helper method or use the existing service. Example:

In `TeamsGraphService.cs` and `MeetingsApiController.cs`, replace:
```csharp
// ❌ Current — reads raw WikiContent (now JSON)
var orgCtx = await db.OrgContexts.FirstOrDefaultAsync(o => o.EntraTenantId == tenantId, ct);
orgWikiContent = orgCtx?.WikiContent;
```
With:
```csharp
// ✅ Fix — use service layer and format as prose
var entries = await _orgContextService.GetContextAsync(tenantId);
orgWikiContent = entries.Count > 0
    ? string.Join("\n", entries.Select(e => $"{e.Term}: {e.Description}"))
    : null;
```
Note: `TeamsGraphService` will need `IOrgContextService` injected via DI. `MeetingsApiController` already has access to the DB context but should also be updated to go through the service.

---

### I2: Three DB round-trips for a single-row read

**Files:** `OrgContextService.cs` (lines 54-75)
**Category:** Performance / design
**Issue:** `GetContextAsync`, `GetUpdatedAtAsync`, and `GetUpdatedByAsync` each open a new `DbContext` and call `FirstOrDefaultAsync` on the same `firm_org_context` row. On every page load (`OrgContext.razor`) and every controller GET, this fires 3 queries where 1 would suffice.
**Fix (future sprint):** Add a `GetContextWithMetaAsync` method returning `(List<OrgContextEntry> entries, DateTime? updatedAt, string? updatedBy)` in a single query. Or return a result record. Not blocking this PR, but flag for backlog.

---

## Nitpicks — 4

**N1 — Legacy term mismatch: "Legacy Content" vs "Content"**
- `OrgContextService.cs:46` wraps as `OrgContextEntry("Legacy Content", content)`
- `OrgContextController.cs:71,76` wraps as `OrgContextEntry(Term: "Content", ...)`
Two code paths for same semantic operation use different term strings. Consumers see inconsistent labels. Pick one — suggest `"Legacy Content"` as it's more descriptive — and align both.

**N2 — `SaveAllAsync` lacks `_isAdmin` guard (defense-in-depth)**
- `OrgContext.razor`: `SaveAllAsync` has no `if (!_isAdmin) return;` guard
- Not exploitable in Blazor Server (component methods aren't HTTP endpoints; Save button is only rendered for admins)
- Trivial 1-line hardening: `if (!_isAdmin) { Snackbar.Add("Unauthorized", Severity.Error); return; }`

**N3 — Empty `_tenantId` shows blank page with no diagnostic**
- `OrgContext.razor:184` — when `_tenantId` is empty, `LoadAsync` silently skips all service calls
- No snackbar, no alert, no user-facing explanation
- Recommend: `Snackbar.Add("Tenant ID not resolved — contact your administrator.", Severity.Warning);`

**N4 — Empty description allowed in dialog**
- `OrgContext.razor:137` — only `_dialogTerm` validated; `_dialogDescription` can be blank
- If intentional, fine. If not, add `&& !string.IsNullOrWhiteSpace(_dialogDescription)` to the `Disabled` binding.

---

## Positive Observations

- **C2 SQL parameterization is exactly right.** `json = JsonSerializer.Serialize(entries, _jsonOpts)` is computed first, then passed as `{1}` to `ExecuteSqlRawAsync`. Clean.
- **Legacy plain-text handling is correct.** The `catch (JsonException)` fall-through logic in `GetContextAsync` is exactly the right pattern — no silent drops, no early returns.
- **`HttpClientFactory` removal is clean.** Zero HTTP client references in the razor component. The DI injection pattern is correct.
- **Controller admin guard is solid.** `[Authorize]` at class level + `if (!IsAdmin()) return Forbid()` before any data access + server-side OID claim comparison. Good defense-in-depth.
- **`IDbContextFactory` usage is correct** throughout — no scoped DbContext lifetime issues.
- **`_saving = false` is in `finally`** — button always re-enables. ✅

---

## What to Fix (NEEDS-CHANGES)

### Required before merge:

**1. Update `TeamsGraphService.cs` — two locations**

Both `ProcessTranscriptAsync` (direct call path, ~line 209) and `FetchAndProcessTranscriptAsync` (~line 359) fetch `orgCtx?.WikiContent` directly. Both need to be updated to use `IOrgContextService` and format entries as prose.

Inject `IOrgContextService _orgContextService` into `TeamsGraphService` constructor. Replace both raw EF reads with:
```csharp
var entries = await _orgContextService.GetContextAsync(tenantId);
orgWikiContent = entries.Count > 0
    ? string.Join("\n", entries.Select(e => $"{e.Term}: {e.Description}"))
    : null;
```

**2. Update `MeetingsApiController.cs` — one location (~line 810)**

Same fix: replace raw `orgCtx?.WikiContent` read with service-layer call + prose formatting. `IOrgContextService` should already be injectable — it's registered in DI (`Program.cs:80`).

---

### Optional (nitpicks, take or leave):

- Align "Legacy Content" / "Content" term name across `OrgContextService` and `OrgContextController`
- Add `if (!_isAdmin) return;` guard at top of `SaveAllAsync`
- Add snackbar for empty `_tenantId` scenario
- Add description validation in dialog if required field is intended
