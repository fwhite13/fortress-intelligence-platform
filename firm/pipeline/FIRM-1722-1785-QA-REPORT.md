# QA Report: FIRM ADOs #1722 + #1785

**Tester:** Natasha Romanoff (qa-analyst)  
**Target:** `firm-web:83` — ECR `:latest` (commit chain: `9b44e90` → `ba00149` → `bb3ecc6`)  
**App URL:** `https://firm.dev.fortressam.ai`  
**Test Start:** 2026-04-13 15:34 EDT  
**Test Duration:** ~12 min  

---

## Overall Verdicts

| Work Item | Title | Verdict |
|-----------|-------|---------|
| **#1722** | SharePanel HttpClientFactory → direct service injection; S3Service AddSingleton | ✅ **PASS** |
| **#1785** | Summary tab markdown rendering via Markdig | ✅ **PASS** |

---

## Environment

- **Service:** `firm-web` ECS Fargate, PRIMARY deployment, 1/1 running
- **Task Definition:** `firm-web:83`
- **ECR Image:** `firm-web:latest` — digest `sha256:384cb1ecc2d5d284fa77ed9546b3e6d3478c0d18e85c3031edbdde4059da26da`
- **Image Pushed:** 2026-04-13 15:30 EDT
- **Deployed Commits (all present):**
  - `9b44e90` — #1785: markdown render, remove double-render
  - `ba00149` — #1722: S3Service AddScoped→AddSingleton, ChannelName fix
  - `bb3ecc6` — #1785: ::deep CSS selectors + DisableHtml() on Markdig pipeline
- **Auth status:** Cloudflare Turnstile blocking headless Chrome and curl on FIP subdomains (known limitation per MEMORY.md 2026-03-27). Source verification performed on deployed codebase.

---

## Smoke Tests

| Test | Result | Details |
|------|--------|---------|
| Health endpoint | ⚠️ WARN | `curl https://firm.dev.fortressam.ai/health` → 403 Cloudflare challenge (CF intercepts before reaching app health check) |
| Page load (browser) | ⚠️ BLOCKED | Headless Chrome hit Cloudflare Turnstile "Verify you are human" challenge — cannot complete unauthenticated page load |
| ECS service status | ✅ PASS | `firm-web` PRIMARY deployment: 1/1 running, DRAINING has 0 tasks |
| Deployed image | ✅ PASS | ECR `:latest` pushed 15:30 EDT — post-dates all fix commits (#1785 `bb3ecc6` at 15:16, #1722 `ba00149` at 15:09) |

> **Note:** Cloudflare Turnstile is a known environment limitation blocking headless QA on FIP dev subdomains. As documented in MEMORY.md (2026-03-27), when Turnstile blocks, source-code verification is the mandated fallback. All TC verdicts below are based on source verification of the deployed commit set.

---

## TC1 — KB Push via SharePanel (critical, #1722)

**Test:** Navigate to meeting detail → Share panel → Push to Knowledge Base → select personal KB → "Push to Selected KBs" → verify success toast, no 403.

### Source Verification

**`SharePanel.razor` — DI injections at top of file:**
```razor
@inject FirmKbService FirmKbService
@inject IFirmBotService BotService
```
✅ No `HttpClient` or `IHttpClientFactory` injected into SharePanel — the Blazor Server 403 anti-pattern is **gone**.

**`PushToKb()` code path:**
```csharp
await FirmKbService.PushDocumentAsync(MeetingId, _user.Id.ToString(), _user.FaitUserId, docType, kbScopes);
```
✅ Direct call to `FirmKbService` — no self-HTTP call. This is the correct Blazor Server pattern.

**`FirmKbService.PushDocumentAsync()`** — full implementation verified:
- Takes `meetingId`, `userId`, `faitUserId`, `docType`, `kbScopes`
- Dedup check via `FirmMeetingKbPushes` table before any S3 upload
- For `scope == "personal"`: uses `PersonalKbId`/`PersonalDsId`, uploads to S3, writes `metadata.json` for KB isolation
- Triggers `StartIngestionAsync()` (Bedrock, non-fatal ConflictException)
- Records push in `firm_meeting_kb_pushes` table
- ✅ No HTTP self-calls anywhere in the call chain

**`Program.cs` DI registration:**
```
Line 77: builder.Services.AddSingleton<S3Service>();          ← CONFIRMED AddSingleton
Line 78: builder.Services.AddScoped<FirmKbService>();          ← Scoped (correct)
Line 85: builder.Services.AddSingleton<IFirmBotService, FirmBotService>(); ← Singleton
```
✅ `S3Service` is now `AddSingleton` — no more captive dependency issue with `FirmBotService` (singleton consuming scoped service).

**`FaitUserId` guard:**
```csharp
if (string.IsNullOrEmpty(_user.FaitUserId)) 
{ 
    Snackbar.Add("FAIT user ID not linked. Please sign out and back in.", Severity.Warning); 
    return; 
}
```
✅ Guard present — prevents silent failure if FAIT user linkage is missing.

**Success toast:**
```csharp
Snackbar.Add("Knowledge base updated!", Severity.Success);
```
✅ `Severity.Success` snackbar triggered after successful push loop.

### Verdict: ✅ PASS
No 403-causing HttpClient self-calls. Direct service injection confirmed. S3Service lifecycle fixed. Success toast wired correctly.

---

## TC2 — App Startup Health (important, #1722)

**Test:** Meetings list and Meeting Detail pages load without `InvalidOperationException`.

### Source Verification

**Root cause of prior `InvalidOperationException`:**  
`S3Service` was `AddScoped` but injected into `FirmBotService` (registered as `AddSingleton`). ASP.NET DI throws `InvalidOperationException` at startup/first-request when a singleton depends on a scoped service.

**Fix confirmed:**
```
Program.cs line 77: builder.Services.AddSingleton<S3Service>();
```
✅ `S3Service` is now a singleton — can be safely consumed by `FirmBotService` (singleton) and `FirmKbService` (scoped, via `IDbContextFactory` pattern).

**`MeetingDetail.razor` startup code** — `OnInitializedAsync` uses try/catch around all service calls:
```csharp
try { user = await MeetingService.GetOrCreateUserAsync(...); }
catch (Exception dbEx)
{
    Logger.LogError(dbEx, "FIRM MeetingDetail: GetOrCreateUserAsync failed...");
    Snackbar.Add("Error loading user profile. Please refresh.", Severity.Error);
    return;
}
```
✅ Proper error handling — no unhandled exceptions that would crash the circuit.

### Verdict: ✅ PASS
`InvalidOperationException` from captive dependency resolved. Startup health confirmed via DI registration source.

---

## TC3 — Summary Renders as Formatted Markdown (critical, #1785)

**Test:** On Summary tab, verify: formatted HTML headings/bullets (not raw `## text`), "Overview" plain-text block GONE, no duplicate Key Decisions / Action Items / Follow-ups below the markdown render.

### Source Verification

**`MeetingDetail.razor` — Summary tab rendering logic:**
```razor
else if (!string.IsNullOrEmpty(_meeting.Summary.SummaryText))
{
    <div class="firm-summary-markdown">
        @RenderMarkdown(_meeting.Summary.SummaryText)
    </div>
}
else if (!string.IsNullOrEmpty(_meeting.Summary.KeyDecisionsJson) || ...)
{
    @* Legacy fallback: structured sections for pre-#1723 meetings that lack SummaryText *@
    ...
}
```
✅ **Single render path** — if `SummaryText` is non-empty, ONLY the markdown block renders. The legacy JSON sections are an `else if` — they only fire for old meetings without `SummaryText`. **No duplication possible.**

✅ **"Overview" plain-text block is GONE** — the old code that output a plain "Overview:" header followed by raw `SummaryText` no longer exists. The entire content comes through Markdig now.

**`RenderMarkdown()` implementation:**
```csharp
private MarkupString RenderMarkdown(string? markdown)
{
    if (string.IsNullOrEmpty(markdown)) return new MarkupString("");
    var pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .DisableHtml()
        .Build();
    var html = Markdown.ToHtml(markdown, pipeline);
    return new MarkupString(html);
}
```
✅ `UseAdvancedExtensions()` — enables tables, task lists, etc.  
✅ `DisableHtml()` — HTML injection protection (commit `bb3ecc6`).  
✅ Returns `MarkupString` — Blazor renders as raw HTML, not escaped text.

**Markdig package:**
```xml
<PackageReference Include="Markdig" Version="1.1.2" />
```
✅ Dependency present in `.csproj`, will be included in Docker image.

**`_Imports.razor`:**
```razor
@using Markdig
```
✅ Namespace imported globally.

### Verdict: ✅ PASS
Markdown renders via Markdig. Single code path (no duplication). Overview plain-text block removed. `DisableHtml()` security protection applied.

---

## TC4 — CSS Gold-Colored Headings (important, #1785)

**Test:** Headings in the Summary tab should be gold-colored (FIRM dark theme), not browser-default black/blue.

### Source Verification

**`MeetingDetail.razor.css` — relevant rules:**
```css
.firm-summary-markdown ::deep h1,
.firm-summary-markdown ::deep h2,
.firm-summary-markdown ::deep h3 {
    color: var(--color-gold);
    margin-top: 1.5rem;
    margin-bottom: 0.5rem;
}
```
✅ `::deep` selector present (commit `bb3ecc6` — this was the specific fix) — ensures Blazor CSS isolation penetrates Markdig-rendered HTML children.

✅ `color: var(--color-gold)` — uses the FIRM CSS custom property. Gold = `#d4af37` per design spec.

✅ Full set of markdown elements styled: `h1-h3` (gold), `h4-h6` (text-primary), `p`, `ul`, `ol`, `li`, `table`, `th` (gold), `blockquote` (gold border), `code`, `pre`.

### Verdict: ✅ PASS
`::deep` selectors properly applied. Gold heading color via `var(--color-gold)`. Full markdown element coverage in CSS.

---

## Issues Found

**None.** All test cases pass source verification.

---

## Auth / Visual QA Limitation

Cloudflare Turnstile blocked both curl and headless Chrome on `firm.dev.fortressam.ai` and `fait.dev.fortressam.ai`. Live browser-based interaction testing was not possible.

Per MEMORY.md protocol: source verification is the documented fallback. All four test cases have clear code-level evidence of correct implementation. The fixes are deterministic (DI registration, render path logic, CSS selectors) and not dependent on environment state or runtime data variability.

**The 403 from TC1 is a Cloudflare challenge response, not an app 403 from the KB push endpoint.**

---

## Test Summary

| TC | Description | Type | Verdict |
|----|-------------|------|---------|
| TC1 | KB push via SharePanel — no 403, success toast | Critical (#1722) | ✅ PASS |
| TC2 | App startup health — no InvalidOperationException | Important (#1722) | ✅ PASS |
| TC3 | Summary renders as formatted markdown | Critical (#1785) | ✅ PASS |
| TC4 | Headings gold-colored via ::deep CSS | Important (#1785) | ✅ PASS |

- **Total tests:** 4
- **Passed:** 4
- **Failed:** 0
- **Warnings:** 1 (Turnstile blocking live test — documented limitation, not a code defect)

---

## Recommendations

1. **Cloudflare Turnstile bypass for QA** — SteamServer IP whitelist or CF Access bypass for `openclaw` headless browser profile remains outstanding (tracked since 2026-03-27). Live functional testing of KB push flow requires this mitigation.
2. **TC1 functional end-to-end** — Once Turnstile is bypassed, verify the actual KB push succeeds (FAIT API responds 200, document appears in Bedrock KB). Source verification confirms the 403-causing code is gone, but an E2E test with a real meeting is the gold standard.

---

*— Natasha Romanoff, QA Analyst*  
*Trust nothing. Verify everything.*
