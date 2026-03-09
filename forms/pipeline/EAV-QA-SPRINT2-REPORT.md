# EAV Form Tools — QA Report (Sprint 1+2)

**Date:** 2026-02-26  
**QA Analyst:** Natasha Romanoff (qa-analyst)  
**App URL:** http://localhost:5200  
**QA Tier:** Sprint QA  

---

## Verdict: ⚠️ WARN

**Summary:** Core infrastructure is solid — REST API is fully functional, home page and navigation render correctly with proper MudBlazor styling. However, a **consistent data-loading failure** on both `/forms` and `/dictionary` means users see error toasts and empty tables instead of their data. Root cause is a single misconfiguration: the default `HttpClient` in Blazor is missing a `BaseAddress`. The fix is a one-liner in `Program.cs`. The 19 seed records are safely in the database; the plumbing to display them from Blazor components is broken.

---

## Test Results

### App Startup
✅ **PASS**

App was not running at test start. Started with `dotnet run --urls "http://0.0.0.0:5200"`. Build completed with NuGet package version warnings (UglyToad.PdfPig approximate matches — non-blocking). App started successfully:

```
Now listening on: http://0.0.0.0:5200
Application started.
```

- HTTP root: **200 OK** (8ms)
- Kestrel Kestrel configuration override warning logged (expected — appsettings.json specifies Kestrel endpoint, overrides CLI `--urls`)
- ExtractionBackgroundService started OK

---

### Form Library (`/forms`)
⚠️ **WARN — Page renders, data load fails**

**UI Elements Present:**
| Element | Status |
|---------|--------|
| Page loads | ✅ |
| "Form Library" heading | ✅ |
| Upload section (Carrier Name input + Form Type dropdown) | ✅ |
| "Upload PDFs" button | ✅ |
| Search input ("Search carrier or form name...") | ✅ |
| Status filter dropdown | ✅ |
| Refresh button | ✅ |
| Table with headers (Carrier, Form Name, Type, Pages, Fields, Status, Uploaded) | ✅ |
| Multi-file indicator / upload queue panel | ⚠️ Not visible — may only appear after initiating upload |

**Issue:** Red error toast on page load:  
> *"Failed to load forms: An invalid request URI was provided. Either the request URI must be an absolute URI or BaseAddress must be set."*

Table shows "No forms uploaded yet" (empty state) — cannot distinguish from a genuine empty DB vs. load failure. Given the REST API confirms an empty forms list, this is functionally consistent, but the error toast is inappropriate and will confuse users.

---

### Data Dictionary (`/dictionary`)
⚠️ **WARN — Page renders, 0 records displayed (should be 19)**

**UI Elements Present:**
| Element | Status |
|---------|--------|
| Page loads | ✅ |
| "Data Dictionary" heading | ✅ |
| "+ Add Field" button | ✅ |
| Search input ("Search field code or name...") | ✅ |
| Category filter dropdown | ✅ |
| Refresh button | ✅ |
| Table with headers (Field Code, Display Name, Category, Type, Actions) | ✅ |
| Footer "0 field(s)" counter | ✅ (shows 0 — incorrect) |

**Issue:** Same red error toast on page load:  
> *"Failed to load dictionary: An invalid request URI was provided. Either the request URI must be an absolute URI or BaseAddress must be set."*

Table shows "No dictionary fields found" — **0 records displayed, but 19 confirmed in DB via API.**

---

### API Smoke Tests
✅ **PASS — All 3 tests pass**

```
GET /api/dictionary    → Dictionary records: 19   ✅ (correct)
GET /api/forms         → {"total":0,"page":1,"pageSize":25,"items":[]}   ✅ (expected empty)
GET /api/nonexistent   → HTTP 404   ✅ (correct)
```

The REST API layer is fully healthy. All data is present in the database. The issue is exclusively in the Blazor component layer.

---

### Visual Assessment
✅ **PASS — MudBlazor renders correctly**

- Dark navy sidebar with icons: Home, Form Library, Question Sets, JSON Generator, Data Dictionary — all 5 visible ✅
- Header bar with Fortress logo + "Form Tools" + "EAV Form Intelligence" subtitle ✅
- MudBlazor components rendering properly (not raw HTML): cards, tables, inputs, dropdowns, buttons ✅
- Home page 3-card feature grid (Upload & Extract, Cross-Reference, Generate JSON) — clean and correct ✅
- "GO TO FORM LIBRARY" CTA button present ✅
- No CSS errors, no unstyled content
- **Error toasts are styled (MudBlazor red snackbar) — at least they look right even if wrong**

---

## Issues Found

| Severity | Description | Page | Recommendation |
|----------|-------------|------|----------------|
| **Important** | Default `HttpClient` injected in Blazor pages has no `BaseAddress` — all `Http.GetFromJsonAsync("api/...")` calls fail with `InvalidOperationException` | `/forms`, `/dictionary` (and likely `/forms/{id}/review`) | In `Program.cs`, add: `builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri("http://localhost:5200/") });` — or use `NavigationManager.BaseUri` for dynamic resolution |
| **Minor** | Upload queue panel (sprint 1 feature) not visible on `/forms` at any viewport — may only appear post-click or may not be implemented in UI | `/forms` | Tony to confirm whether queue panel should be visible before upload or only after initiating |
| **Minor** | Blazor WebSocket initial connection failure on first page load (ERR_CONNECTION_REFUSED on `/_blazor/negotiate`), then auto-reconnect succeeds | All pages | Likely a startup timing issue — app is still initializing when browser hits it. Consider adding a startup warmup or health check gate. Self-resolves in ~2s. |
| **Minor** | NuGet package version warnings at build time for `UglyToad.PdfPig` (approximate matches, not exact) | Build | Non-blocking but warrants pinning exact versions before production |

---

## Root Cause Analysis: HttpClient Issue

**What's happening:**  
`DataDictionary.razor`, `FormLibrary.razor`, `FormReview.razor`, and others use `@inject HttpClient Http` (the unnamed/default `HttpClient`). They call relative URLs like `Http.GetFromJsonAsync<List<DictionaryField>>("api/dictionary?...")`.

**`Program.cs` registers:**
```csharp
builder.Services.AddHttpClient("FortressApi", client => { ... });  // ← Named, for external Fortress API
// ← Default HttpClient NOT registered — no BaseAddress
```

**The fix (one line):**
```csharp
// Add BEFORE app.Build() in Program.cs:
builder.Services.AddScoped(sp => new HttpClient 
{ 
    BaseAddress = new Uri("http://localhost:5200/") 
});
```

Or, for environment-agnostic resolution (recommended):
```csharp
builder.Services.AddScoped(sp => {
    var config = sp.GetRequiredService<IConfiguration>();
    var url = config["Kestrel:Endpoints:Http:Url"] ?? "http://localhost:5200";
    return new HttpClient { BaseAddress = new Uri(url + "/") };
});
```

---

## Screenshots Captured

| Page | File | Description |
|------|------|-------------|
| Home (mobile 412px) | `40828276-f596-4073-9805-b3c5aaf64d25.png` | Clean render, 3-card layout, CTA button |
| `/forms` (mobile 412px) | `ff199d35-d3cd-4de9-b437-4882293a658a.png` | Error toast visible, upload UI elements present |
| Home (desktop 1280px) | `29fbd149-6052-4ba3-88c4-a7f8260fd663.png` | Full sidebar visible, 3-column card grid |
| `/forms` (desktop 1280px) | `972cb70c-8d26-4e95-bc0c-f59adaa4c77a.png` | Error toast + table structure |
| `/dictionary` (desktop 1280px) | `d08f171f-d79a-4103-a3cd-9eba6e448ae2.png` | Error toast, 0 fields shown |

---

## Viewport Tests

| Viewport | Size | Nav Type | Layout | Overflow | Result |
|----------|------|----------|--------|----------|--------|
| Mobile | 393×852 | Hamburger menu (top) ✅ | Single column ✅ | None ✅ | ✅ PASS |
| Desktop | 1280×800 | Left sidebar ✅ | Multi-column cards ✅ | None ✅ | ✅ PASS |

---

## Notes for Sprint 3

1. **Fix HttpClient BaseAddress first** — This is the Sprint 3 blocker. Every data-loading Blazor page will fail until this is resolved. It's a 2-minute fix but blocks all functional testing of the review UI and dictionary CRUD.

2. **Upload queue panel** — The `/forms` page shows the upload *form* (Carrier Name + Form Type + Upload button) but no upload *queue* panel was visible at any viewport. Sprint 1 spec mentions a "non-blocking upload queue" — Tony should verify whether the queue panel is supposed to render before any upload is initiated, or only after.

3. **`/forms/{id}/review` untestable** — The side-by-side review UI (Sprint 2's main deliverable) couldn't be tested because no forms exist in the library and the forms page fails to load. Once the HttpClient fix lands, upload a real carrier PDF and verify the PDF viewer + editable field list renders correctly.

4. **Fortress API credentials in `appsettings.Development.json`** — Credentials are committed to disk in plaintext. Before any wider deployment, move to user-secrets or environment variables.

5. **Blazor WebSocket retry on startup** — The initial WebSocket negotiation fails then self-heals in ~2s. This will cause a flash of unrendered state if the page is loaded immediately after startup. Consider a `<LoadingIndicator>` or debounced initialization.

6. **Question Sets + JSON Generator routes** — Both appear in the nav sidebar but weren't in the Sprint 1/2 scope. Navigate to them in Sprint 3 QA to confirm they either load or redirect gracefully (not 404 or crash).
